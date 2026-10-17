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

    public SystemApiKeyManagementService(IApiKeyAdministrationService inner, IHttpContextAccessor httpContextAccessor = null, IOptions<ApiKeyOptions> options = null, IUserService userService = null)
    {
        _inner = inner;
        _httpContextAccessor = httpContextAccessor;
        _userService = userService;
        _ownership = options?.Value?.SystemKeys ?? ApiKeyOwnership.Both;
    }

    public async IAsyncEnumerable<IApiKey> GetSystemKeysAsync(PrivateKeyScope personalKeys = PrivateKeyScope.None)
    {
        var callerUserKey = personalKeys == PrivateKeyScope.None ? null : await GetCallerUserKeyAsync();
        var canOversee = CanOversee();

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
        if (!personal) return await _inner.CreateSystemKeyAsync(name, scopes, expiryDate, GetCurrentUserIdentity());

        // Anti-spoof: a caller can only ever create a key owned by themselves.
        var owner = await GetCallerUserKeyAsync()
                    ?? throw new InvalidOperationException("Cannot create a personal system API key: the caller is not a signed-in user.");
        return await _inner.CreatePersonalSystemKeyAsync(name, scopes, owner, expiryDate, GetCurrentUserIdentity());
    }

    public async Task<IApiKey> RefreshSystemKeyAsync(string key)
    {
        await EnsureCanMutateAsync(key);
        return await _inner.RefreshSystemKeyAsync(key);
    }

    public async Task LockSystemKeyAsync(string key)
    {
        await EnsureCanMutateAsync(key);
        await _inner.LockSystemKeyAsync(key);
    }

    public async Task SetSystemKeyDisabledAsync(string key, bool disabled)
    {
        await EnsureCanMutateAsync(key);
        await _inner.SetSystemKeyDisabledAsync(key, disabled, GetCurrentUserIdentity());
    }

    public async Task DeleteSystemKeyAsync(string key)
    {
        await EnsureCanMutateAsync(key);
        await _inner.DeleteSystemKeyAsync(key);
    }

    /// <summary>
    /// Guards mutation of a personal system key: only its owner, or a caller with oversight, may refresh,
    /// lock, disable or delete it. Shared keys (and unknown keys) pass through.
    /// </summary>
    private async Task EnsureCanMutateAsync(string key)
    {
        IApiKey target = null;
        await foreach (var k in _inner.GetSystemKeysAsync())
        {
            if (k.Key == key) { target = k; break; }
        }

        if (target == null || string.IsNullOrEmpty(target.OwnerMemberKey)) return; // shared — inner handles validity
        if (CanOversee()) return;

        var callerUserKey = await GetCallerUserKeyAsync();
        if (callerUserKey != null && target.OwnerMemberKey == callerUserKey) return;

        throw new UnauthorizedAccessException("This system API key is personal to another user and can only be managed by its owner.");
    }

    /// <summary>The signed-in caller's user key, or null when the caller is not a person.</summary>
    /// <remarks>
    /// <b>An API key is not a person</b>, so a caller authenticated by one can neither own a personal key nor
    /// see one as its owner — even if the store would resolve a user record for its principal.
    /// </remarks>
    private async Task<string> GetCallerUserKeyAsync()
    {
        var user = _httpContextAccessor?.HttpContext?.User;
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
    private bool CanOversee()
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        return user != null && user.HasClaim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee);
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
