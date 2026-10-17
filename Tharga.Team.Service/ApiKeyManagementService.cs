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
    private readonly ApiKeyOptions _options;

    public ApiKeyManagementService(IApiKeyAdministrationService inner, IHttpContextAccessor httpContextAccessor = null, IOptions<ApiKeyOptions> options = null)
    {
        _inner = inner;
        _httpContextAccessor = httpContextAccessor;
        _options = options?.Value;
    }

    public async IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey, PrivateKeyScope privateKeys = PrivateKeyScope.None, bool allowPrivileged = false)
    {
        var callerMemberKey = GetCallerMemberKey();
        var canOversee = CanOversee();
        var privilegedAllowed = allowPrivileged && IsPrivileged();

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

    public Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, bool ownerScoped = false)
    {
        // Checked here as well as in the default administration service, so the UI is held to it even when a
        // host registers its own IApiKeyAdministrationService.
        PrivateKeyRequirement.Ensure(_options, ownerScoped);

        string ownerMemberKey = null;
        if (ownerScoped)
        {
            // Anti-spoof: a caller can only ever mint a key owned by themselves.
            ownerMemberKey = GetCallerMemberKey()
                ?? throw new InvalidOperationException("Cannot create an owner-scoped API key: the caller has no team-member context.");
        }

        return _inner.CreateKeyAsync(teamKey, name, accessLevel, roles, scopeOverrides, expiryDate, tags, GetCurrentUserIdentity(), ownerMemberKey);
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
        await EnsureCanMutateAsync(teamKey, key);
        await _inner.SetScopeOverridesAsync(teamKey, key, scopes);
    }

    public async Task SetRolesAsync(string teamKey, string key, string[] roles)
    {
        await EnsureCanMutateAsync(teamKey, key);
        await _inner.SetRolesAsync(teamKey, key, roles);
    }

    /// <summary>
    /// Guards mutation of an owner-scoped (private) key: only the owner, or a caller with oversight (audit
    /// escape), may recycle/lock/delete/edit it. Team-wide keys (and unknown keys) pass through.
    /// </summary>
    private async Task EnsureCanMutateAsync(string teamKey, string key)
    {
        IApiKey target = null;
        await foreach (var k in _inner.GetKeysAsync(teamKey))
        {
            if (k.Key == key) { target = k; break; }
        }

        if (target == null || string.IsNullOrEmpty(target.OwnerMemberKey)) return; // not private — inner handles ownership/validity

        var callerMemberKey = GetCallerMemberKey();
        var isOwner = callerMemberKey != null && target.OwnerMemberKey == callerMemberKey;
        if (isOwner || CanOversee()) return;

        throw new UnauthorizedAccessException("This API key is private to another member and can only be managed by its owner.");
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

    private bool IsPrivileged()
    {
        var value = _httpContextAccessor?.HttpContext?.User?.FindFirst(TeamClaimTypes.AccessLevel)?.Value;
        return Enum.TryParse<AccessLevel>(value, out var level) && level is AccessLevel.Owner or AccessLevel.Administrator;
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
