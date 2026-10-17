namespace Tharga.Team;

/// <summary>
/// User-facing management of a team's API keys. Every operation names the team it acts on as its first
/// argument, and requires the <see cref="ApiKeyScopes.Manage"/> scope <b>on that team</b> — holding the
/// scope for one team does not authorize acting on another.
/// </summary>
/// <remarks>
/// System keys, which belong to no team, live on <see cref="ISystemApiKeyManagementService"/>.
/// </remarks>
public interface IApiKeyManagementService
{
    /// <summary>
    /// Returns the team's API keys for the caller. Team-wide keys are always included; owner-scoped
    /// ("private", shown as "Personal") keys are included per <paramref name="privateKeys"/>, intersected with
    /// the caller's entitlement (owner sees own; an <see cref="SystemApiKeyScopes.Oversee"/> holder sees all;
    /// <paramref name="allowPrivileged"/> additionally lets Administrator/Owner see private keys). Defaults
    /// preserve the original team-wide-only behaviour.
    /// </summary>
    [RequireScope(ApiKeyScopes.Manage)]
    IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey, PrivateKeyScope privateKeys = PrivateKeyScope.None, bool allowPrivileged = false);

    /// <summary>Creates a new API key. When <paramref name="ownerScoped"/> is true the key is private to the caller (owner = the caller's team-member key); a caller can only ever mint a key owned by themselves.</summary>
    /// <remarks>
    /// <see cref="ApiKeyOptions.TeamKeys"/> decides what is allowed: <see cref="ApiKeyOwnership.Shared"/> refuses
    /// <paramref name="ownerScoped"/> with an <see cref="InvalidOperationException"/>, and
    /// <see cref="ApiKeyOwnership.Personal"/> makes every key private whatever <paramref name="ownerScoped"/> says.
    /// </remarks>
    [RequireScope(ApiKeyScopes.Manage)]
    Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, bool ownerScoped = false);

    [RequireScope(ApiKeyScopes.Manage)]
    Task<IApiKey> RefreshKeyAsync(string teamKey, string key);

    [RequireScope(ApiKeyScopes.Manage)]
    Task LockKeyAsync(string teamKey, string key);

    /// <summary>
    /// Disables the key, or enables it again. A disabled key is refused at authentication but keeps its
    /// name, scopes, roles, tags and history — the reversible alternative to <see cref="DeleteKeyAsync"/>.
    /// </summary>
    /// <remarks>
    /// <b>Refreshing a disabled key does not enable it.</b> A refresh mints a new secret; it is not a
    /// decision to trust the key again, and the usual reason to refresh is the same suspected leak that
    /// prompted the disable.
    /// </remarks>
    [RequireScope(ApiKeyScopes.Manage)]
    Task SetKeyDisabledAsync(string teamKey, string key, bool disabled);

    [RequireScope(ApiKeyScopes.Manage)]
    Task DeleteKeyAsync(string teamKey, string key);

    [RequireScope(ApiKeyScopes.Manage)]
    Task SetScopeOverridesAsync(string teamKey, string key, string[] scopes);

    [RequireScope(ApiKeyScopes.Manage)]
    Task SetRolesAsync(string teamKey, string key, string[] roles);
}
