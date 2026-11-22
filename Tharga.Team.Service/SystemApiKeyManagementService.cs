using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Delegates system API key operations to <see cref="IApiKeyAdministrationService"/>, stamping the
/// creating user's identity from their claims rather than trusting a caller-supplied value, and enforcing
/// owner-scoping for personal system keys.
/// </summary>
/// <remarks>
/// Split out of <c>ApiKeyManagementService</c>: these operations belong to no team, so they are registered
/// as a system service. Keeping them alongside the team operations is what previously let one
/// authorization policy be applied to both.
/// <para>
/// <b>A personal system key is owned by a user, not a team member.</b> The owner is the caller's user key,
/// which is the same value as their member key in any team — so the member key is used when a team is
/// selected, and the user store is asked otherwise.
/// </para>
/// </remarks>
public class SystemApiKeyManagementService : ISystemApiKeyManagementService
{
    private readonly IApiKeyAdministrationService _inner;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserService _userService;
    private readonly ApiKeyOwnership _ownership;
    private readonly SystemGrantCeiling _ceiling;
    private readonly ITeamPrincipalAccessor _principalAccessor;

    public SystemApiKeyManagementService(IApiKeyAdministrationService inner, IHttpContextAccessor httpContextAccessor = null, IOptions<ApiKeyOptions> options = null, IUserService userService = null, ISystemScopeRegistry systemScopeRegistry = null, ITeamPrincipalAccessor principalAccessor = null)
    {
        _inner = inner;
        _httpContextAccessor = httpContextAccessor;
        _userService = userService;
        _ownership = options?.Value?.SystemKeys ?? ApiKeyOwnership.Both;
        _ceiling = new SystemGrantCeiling(systemScopeRegistry);
        _principalAccessor = principalAccessor;
    }

    public async IAsyncEnumerable<IApiKey> GetSystemKeysAsync(PrivateKeyScope personalKeys = PrivateKeyScope.None)
    {
        var caller = await GetCallerAsync();
        var callerUserKey = personalKeys == PrivateKeyScope.None ? null : await GetCallerUserKeyAsync(caller);
        var canOversee = CanOversee(caller);

        await foreach (var key in _inner.GetSystemKeysAsync())
        {
            if (string.IsNullOrEmpty(key.OwnerMemberKey))
            {
                yield return key;
                continue;
            }

            if (personalKeys == PrivateKeyScope.None) continue;

            var isOwner = callerUserKey != null && key.OwnerMemberKey == callerUserKey;
            if (isOwner || (personalKeys == PrivateKeyScope.All && canOversee)) yield return key;
        }
    }

    public async Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, bool personal = false)
    {
        personal = ApiKeyManagementService.ApplyOwnership(_ownership, personal, "system");
        var caller = await GetCallerAsync();
        _ceiling.EnsureCanGrant(caller, scopes);

        if (!personal) return await _inner.CreateSystemKeyAsync(name, scopes, expiryDate, IdentityOf(caller));

        // Anti-spoof: a caller can only ever create a key owned by themselves.
        var owner = await GetCallerUserKeyAsync(caller)
                    ?? throw new InvalidOperationException("Cannot create a personal system API key: the caller is not a signed-in user.");
        return await _inner.CreatePersonalSystemKeyAsync(name, scopes, owner, expiryDate, IdentityOf(caller));
    }

    /// <remarks>
    /// <b>Both halves of the ceiling, and the key is looked up first</b>: who may touch a personal key is decided before anything about its scopes is
    /// said, so a refusal does not reveal what another person's key holds. The new set must be registered and held by the caller, and the key's present
    /// set must be too, so somebody holding little cannot lower a key that holds more.
    /// </remarks>
    public async Task SetSystemKeyScopesAsync(string key, string[] scopes)
    {
        var caller = await GetCallerAsync();
        var target = await FindAsync(key);
        await EnsureCanMutateAsync(target, caller);

        _ceiling.EnsureCanGrant(caller, scopes);
        if (target != null) _ceiling.EnsureCanChange(caller, target.SystemScopes);

        await _inner.SetSystemKeyScopesAsync(key, scopes);
    }

    public async Task<IApiKey> RefreshSystemKeyAsync(string key)
    {
        await EnsureCanManageAsync(key);
        return await _inner.RefreshSystemKeyAsync(key);
    }

    public async Task LockSystemKeyAsync(string key)
    {
        await EnsureCanManageAsync(key);
        await _inner.LockSystemKeyAsync(key);
    }

    public async Task SetSystemKeyDisabledAsync(string key, bool disabled)
    {
        await EnsureCanManageAsync(key);
        await _inner.SetSystemKeyDisabledAsync(key, disabled, IdentityOf(await GetCallerAsync()));
    }

    public async Task DeleteSystemKeyAsync(string key)
    {
        await EnsureCanManageAsync(key);
        await _inner.DeleteSystemKeyAsync(key);
    }

    /// <summary>
    /// Refresh, lock, disable and delete act on a key as a whole, so they are held to the ceiling as changing its scopes is: ownership first, so a refusal
    /// reveals nothing of another person's key, then that the caller holds everything the key does. Otherwise somebody holding little could delete, lock
    /// or replace the secret of a key that holds more (Tharga/Team#522).
    /// </summary>
    private async Task EnsureCanManageAsync(string key)
    {
        var caller = await GetCallerAsync();
        var target = await FindAsync(key);
        await EnsureCanMutateAsync(target, caller);
        if (target != null) _ceiling.EnsureCanChange(caller, target.SystemScopes);
    }

    private async Task<IApiKey> FindAsync(string key)
    {
        await foreach (var k in _inner.GetSystemKeysAsync())
        {
            if (k.Key == key) return k;
        }

        return null;
    }

    /// <summary>
    /// Guards mutation of a personal system key: only its owner, or a caller with oversight, may refresh,
    /// lock, disable or delete it. Shared keys (and unknown keys) pass through.
    /// </summary>
    private async Task EnsureCanMutateAsync(IApiKey target, ClaimsPrincipal caller)
    {
        if (target == null || string.IsNullOrEmpty(target.OwnerMemberKey)) return; // shared — inner handles validity
        if (CanOversee(caller)) return;

        var callerUserKey = await GetCallerUserKeyAsync(caller);
        if (callerUserKey != null && target.OwnerMemberKey == callerUserKey) return;

        throw new UnauthorizedAccessException("This system API key is personal to another user and can only be managed by its owner.");
    }

    /// <summary>
    /// The caller, from the team principal accessor (a Blazor circuit has no request), else the request.
    /// </summary>
    /// <remarks>
    /// <b>Not the request alone.</b> In an interactive circuit <c>HttpContext</c> is the circuit's first request or nothing, so reading the caller from it
    /// made a personal system key impossible to create from the page, and hid a person's own personal keys from them (Tharga/Team#550).
    /// </remarks>
    private async ValueTask<ClaimsPrincipal> GetCallerAsync()
        => (_principalAccessor != null ? await _principalAccessor.GetCurrentAsync() : null)
           ?? _httpContextAccessor?.HttpContext?.User;

    /// <summary>The signed-in caller's user key, or null when the caller is not a person.</summary>
    /// <remarks>
    /// <b>An API key is not a person</b>, so a caller authenticated by one can neither own a personal key nor
    /// see one as its owner — even if the store would resolve a user record for its principal.
    /// </remarks>
    private async Task<string> GetCallerUserKeyAsync(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        if (user.HasClaim(c => c.Type == TeamClaimTypes.ApiKeyId)) return null;

        var memberKey = user.FindFirst(TeamClaimTypes.MemberKey)?.Value;
        if (!string.IsNullOrEmpty(memberKey)) return memberKey;

        if (_userService == null) return null;
        var current = await _userService.GetCurrentUserAsync(user);
        return string.IsNullOrEmpty(current?.Key) ? null : current.Key;
    }

    /// <summary>
    /// Whether the caller may see and manage other users' personal system keys: the
    /// <see cref="SystemApiKeyScopes.Oversee"/> system scope, read as a system grant only.
    /// </summary>
    private static bool CanOversee(ClaimsPrincipal user)
        => user != null && user.HasClaim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee);

    private static string IdentityOf(ClaimsPrincipal user)
        => user?.FindFirst(ClaimTypes.Name)?.Value
           ?? user?.FindFirst("preferred_username")?.Value
           ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? user?.FindFirst("name")?.Value;
}
