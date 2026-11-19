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
    /// <para>
    /// <b>The caller may hand out only what they hold</b> (Tharga/Team#522, as #409 did for team keys): every scope must be registered with
    /// <see cref="ISystemScopeRegistry"/> (<see cref="InvalidOperationException"/>), and one the caller does not hold themselves is refused with an
    /// <see cref="UnauthorizedAccessException"/>. A caller holding <see cref="SystemApiKeyScopes.Oversee"/> may grant any registered scope.
    /// </para>
    /// </remarks>
    [RequireScope(ApiKeyScopes.SystemManage)]
    Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, bool personal = false);

    /// <summary>
    /// Replaces the scopes of a system key, keeping its value, so a client using the key goes on working with the new scopes.
    /// </summary>
    /// <param name="key">The key's id.</param>
    /// <param name="scopes">The scopes the key holds from now on; empty leaves it with none.</param>
    /// <remarks>
    /// The same rules creating a key applies, and refused here rather than only hidden by a picker: every scope must be registered with
    /// <see cref="ISystemScopeRegistry"/> (<see cref="InvalidOperationException"/>), and the caller may grant only scopes they hold themselves
    /// (<see cref="UnauthorizedAccessException"/>). A caller holding <see cref="SystemApiKeyScopes.Oversee"/> may grant any registered scope. A key
    /// that holds a scope the caller does not is not theirs to change either.
    /// <para>
    /// A personal key can be changed only by its owner or an <see cref="SystemApiKeyScopes.Oversee"/> holder. Audited with the scopes added and
    /// removed. The default throws, so a host's own implementation keeps compiling.
    /// </para>
    /// </remarks>
    [RequireScope(ApiKeyScopes.SystemManage)]
    Task SetSystemKeyScopesAsync(string key, string[] scopes)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(SetSystemKeyScopesAsync)}.");

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
