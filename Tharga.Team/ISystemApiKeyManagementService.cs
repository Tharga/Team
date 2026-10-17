namespace Tharga.Team;

/// <summary>
/// User-facing management of **system** API keys — keys not bound to any team. All methods require the
/// <see cref="ApiKeyScopes.SystemManage"/> scope and no team need be selected.
/// </summary>
/// <remarks>
/// Separate from <see cref="IApiKeyManagementService"/> so each interface is wholly one kind: every
/// operation there names the team it acts on, every operation here acts on none. That homogeneity is what
/// lets the registration declare the rule once — <c>AddSystemService</c> here, <c>AddTeamService</c> there
/// — instead of leaving it to a per-method annotation somebody has to remember on the next method added.
/// </remarks>
public interface ISystemApiKeyManagementService
{
    /// <summary>
    /// Returns the system keys for the caller. Shared keys are always included; personal keys are included
    /// per <paramref name="personalKeys"/>, intersected with the caller's entitlement (<c>Mine</c> = the
    /// caller's own; <c>All</c> = own, plus everyone's for an <see cref="SystemApiKeyScopes.Oversee"/> holder).
    /// The default lists shared keys only, as before personal system keys existed.
    /// </summary>
    [RequireScope(ApiKeyScopes.SystemManage)]
    IAsyncEnumerable<IApiKey> GetSystemKeysAsync(PrivateKeyScope personalKeys = PrivateKeyScope.None);

    /// <summary>
    /// Creates a system key. When <paramref name="personal"/> is true the key belongs to the caller, who must
    /// be a signed-in user; a caller can only ever create a key owned by themselves.
    /// </summary>
    /// <remarks>
    /// <see cref="ApiKeyOptions.SystemKeys"/> decides what is allowed: <see cref="ApiKeyOwnership.Shared"/>
    /// refuses <paramref name="personal"/> with an <see cref="InvalidOperationException"/>, and
    /// <see cref="ApiKeyOwnership.Personal"/> makes every key personal whatever <paramref name="personal"/> says.
    /// </remarks>
    [RequireScope(ApiKeyScopes.SystemManage)]
    Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, bool personal = false);

    /// <remarks>A personal key can be refreshed only by its owner or an <see cref="SystemApiKeyScopes.Oversee"/> holder; the same goes for lock, disable and delete.</remarks>
    [RequireScope(ApiKeyScopes.SystemManage)]
    Task<IApiKey> RefreshSystemKeyAsync(string key);

    [RequireScope(ApiKeyScopes.SystemManage)]
    Task LockSystemKeyAsync(string key);

    /// <inheritdoc cref="IApiKeyManagementService.SetKeyDisabledAsync"/>
    [RequireScope(ApiKeyScopes.SystemManage)]
    Task SetSystemKeyDisabledAsync(string key, bool disabled);

    [RequireScope(ApiKeyScopes.SystemManage)]
    Task DeleteSystemKeyAsync(string key);
}
