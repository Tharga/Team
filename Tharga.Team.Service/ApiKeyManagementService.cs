using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Delegates to <see cref="IApiKeyAdministrationService"/> and enforces caller-scoped authorization on
/// top: scope enforcement via <see cref="ScopeProxy{T}"/>, plus owner-scoping for private API keys
/// (resolved from the authenticated principal's claims — not trusting any caller-supplied value).
/// </summary>
public class ApiKeyManagementService : IApiKeyManagementService
{
    private readonly IApiKeyAdministrationService _inner;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApiKeyOwnership _ownership;
    private readonly ITeamPrincipalAccessor _principalAccessor;
    private readonly GrantCeiling _ceiling;

    public ApiKeyManagementService(IApiKeyAdministrationService inner, IHttpContextAccessor httpContextAccessor = null, IOptions<ApiKeyOptions> options = null, IScopeRegistry scopeRegistry = null, ITenantRoleService tenantRoleService = null, ITeamPrincipalAccessor principalAccessor = null)
    {
        _inner = inner;
        _httpContextAccessor = httpContextAccessor;
        _ownership = options?.Value?.TeamKeys ?? ApiKeyOwnership.Both;
        _principalAccessor = principalAccessor;
        _ceiling = new GrantCeiling(scopeRegistry, tenantRoleService, PrincipalKind.ApiKey);
    }

    public async IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey, PrivateKeyScope privateKeys = PrivateKeyScope.None, bool allowPrivileged = false)
    {
        var callerMemberKey = GetCallerMemberKey();
        var canOversee = CanOversee();
        var privilegedAllowed = allowPrivileged && IsPrivileged(teamKey);

        await foreach (var key in _inner.GetKeysAsync(teamKey))
        {
            // Team-wide keys are always visible (subject to the apikey:manage scope already enforced).
            if (string.IsNullOrEmpty(key.OwnerMemberKey))
            {
                yield return key;
                continue;
            }

            if (privateKeys == PrivateKeyScope.None) continue;

            var isOwner = callerMemberKey != null && key.OwnerMemberKey == callerMemberKey;
            if (privateKeys == PrivateKeyScope.Mine)
            {
                if (isOwner) yield return key;
                continue;
            }

            // PrivateKeyScope.All — own + oversight (apikeys:oversee) + privileged (when the host opted in).
            if (isOwner || canOversee || privilegedAllowed) yield return key;
        }
    }

    public async Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, bool ownerScoped = false)
    {
        // A key is never an Owner: ownership is a person's, moved by transfer, and a credential that carries it
        // cannot be traced to anyone who could be asked to give it up.
        if (accessLevel == AccessLevel.Owner)
            throw new InvalidOperationException("An API key cannot be given the Owner level. Use Administrator, or a narrower level with roles and scope overrides.");

        await _ceiling.EnsureCanGrantAsync(await GetCallerAsync(), teamKey, accessLevel, roles, scopeOverrides, oversightExempts: true);

        ownerScoped = ApplyOwnership(_ownership, ownerScoped, "team");

        string ownerMemberKey = null;
        if (ownerScoped)
        {
            // Anti-spoof: a caller can only ever mint a key owned by themselves.
            ownerMemberKey = GetCallerMemberKey()
                ?? throw new InvalidOperationException("Cannot create an owner-scoped API key: the caller has no team-member context.");
        }

        return await _inner.CreateKeyAsync(teamKey, name, accessLevel, roles, scopeOverrides, expiryDate, tags, GetCurrentUserIdentity(), ownerMemberKey);
    }

    public async Task<IApiKey> RefreshKeyAsync(string teamKey, string key)
    {
        await EnsureCanMutateAsync(teamKey, key);
        return await _inner.RefreshKeyAsync(teamKey, key);
    }

    public async Task LockKeyAsync(string teamKey, string key)
    {
        await EnsureCanMutateAsync(teamKey, key);
        await _inner.LockKeyAsync(teamKey, key);
    }

    public async Task SetKeyDisabledAsync(string teamKey, string key, bool disabled)
    {
        await EnsureCanMutateAsync(teamKey, key);
        await _inner.SetKeyDisabledAsync(teamKey, key, disabled, GetCurrentUserIdentity());
    }

    public async Task DeleteKeyAsync(string teamKey, string key)
    {
        await EnsureCanMutateAsync(teamKey, key);
        await _inner.DeleteKeyAsync(teamKey, key);
    }

    public async Task SetScopeOverridesAsync(string teamKey, string key, string[] scopes)
    {
        var target = await EnsureCanMutateAsync(teamKey, key);
        await _ceiling.EnsureCanGrantAsync(await GetCallerAsync(), teamKey, LevelOf(target), target?.Roles, scopes, oversightExempts: true);
        await _inner.SetScopeOverridesAsync(teamKey, key, scopes);
    }

    public async Task SetRolesAsync(string teamKey, string key, string[] roles)
    {
        var target = await EnsureCanMutateAsync(teamKey, key);
        await _ceiling.EnsureCanGrantAsync(await GetCallerAsync(), teamKey, LevelOf(target), roles, target?.ScopeOverrides, oversightExempts: true);
        await _inner.SetRolesAsync(teamKey, key, roles);
    }

    /// <summary>
    /// Guards mutation of a key. Two rules: an owner-scoped (private) key is only its owner's, or an overseer's;
    /// and a key that holds more than the caller cannot be recycled, locked, disabled, deleted or re-scoped by
    /// them (Tharga/Team#409) - refreshing one would hand the caller its secret. Unknown keys pass through, the
    /// inner service handles validity. Returns the key, or null when unknown.
    /// </summary>
    private async Task<IApiKey> EnsureCanMutateAsync(string teamKey, string key)
    {
        IApiKey target = null;
        await foreach (var k in _inner.GetKeysAsync(teamKey))
        {
            if (k.Key == key) { target = k; break; }
        }

        if (target == null) return null;

        if (!string.IsNullOrEmpty(target.OwnerMemberKey))
        {
            var callerMemberKey = GetCallerMemberKey();
            var isOwner = callerMemberKey != null && target.OwnerMemberKey == callerMemberKey;
            if (!isOwner && !CanOversee())
                throw new UnauthorizedAccessException("This API key is private to another member and can only be managed by its owner.");
        }

        await _ceiling.EnsureCanChangeAsync(await GetCallerAsync(), teamKey, LevelOf(target), target.Roles, target.ScopeOverrides, oversightExempts: true);

        return target;
    }

    /// <summary>A key with no level is an Administrator (see <see cref="IApiKey.AccessLevel"/>), so it is checked as one.</summary>
    private static AccessLevel LevelOf(IApiKey key) => key?.AccessLevel ?? AccessLevel.Administrator;

    /// <summary>The caller: from the team principal accessor (a Blazor circuit has no request), else the request.</summary>
    private async ValueTask<ClaimsPrincipal> GetCallerAsync()
        => (_principalAccessor != null ? await _principalAccessor.GetCurrentAsync() : null)
           ?? _httpContextAccessor?.HttpContext?.User;

    /// <summary>
    /// Whether a new key is personal, once the host's <see cref="ApiKeyOwnership"/> has had its say.
    /// </summary>
    /// <remarks>
    /// <b>Asymmetric on purpose; both directions fail safe.</b> Under <see cref="ApiKeyOwnership.Personal"/> a
    /// shared request becomes personal, which can only hide the key from others. Under
    /// <see cref="ApiKeyOwnership.Shared"/> a personal request is refused rather than quietly made shared,
    /// which would show the key to everyone its creator meant to keep it from.
    /// </remarks>
    internal static bool ApplyOwnership(ApiKeyOwnership ownership, bool personal, string kind)
    {
        return ownership switch
        {
            ApiKeyOwnership.Personal => true,
            ApiKeyOwnership.Shared when personal => throw new InvalidOperationException($"Personal {kind} API keys are turned off on this site."),
            _ => personal,
        };
    }

    private string GetCallerMemberKey()
        => _httpContextAccessor?.HttpContext?.User?.FindFirst(TeamClaimTypes.MemberKey)?.Value;

    /// <summary>
    /// Whether the caller may see and manage other members' private keys: the
    /// <see cref="SystemApiKeyScopes.Oversee"/> system scope.
    /// </summary>
    /// <remarks>
    /// The scope is read as a <i>system</i> grant (<see cref="TeamClaimTypes.SystemScope"/>), never as a team
    /// scope, so a team role cannot be configured to reach other members' keys. Before 4.0 the <c>Developer</c>
    /// role also granted this, hard-coded; a host that wants that back maps the scope to the role.
    /// </remarks>
    private bool CanOversee()
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        if (user == null) return false;
        return user.HasClaim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee);
    }

    /// <summary>Whether the caller is an Owner or Administrator <b>of <paramref name="teamKey"/></b>: the level in that team, not another team's.</summary>
    private bool IsPrivileged(string teamKey)
    {
        var level = TeamScopePolicy.AccessLevelFor(_httpContextAccessor?.HttpContext?.User, teamKey);

        return level is AccessLevel.Owner or AccessLevel.Administrator;
    }

    private string GetCurrentUserIdentity()
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        return user?.FindFirst(ClaimTypes.Name)?.Value
               ?? user?.FindFirst("preferred_username")?.Value
               ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user?.FindFirst("name")?.Value;
    }
}
