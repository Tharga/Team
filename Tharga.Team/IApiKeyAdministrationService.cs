namespace Tharga.Team;

/// <summary>
/// Service for managing and validating API keys.
/// </summary>
public interface IApiKeyAdministrationService
{
    /// <summary>Looks up an API key by its raw value. Returns <c>null</c> if no match is found.</summary>
    Task<IApiKey> GetByApiKeyAsync(string apiKey);

    /// <summary>Returns all API keys for the specified team, creating default keys if fewer than AutoKeyCount exist.</summary>
    IAsyncEnumerable<IApiKey> GetKeysAsync(string teamKey);

    /// <summary>Creates a new API key with the specified settings (advanced mode). <paramref name="tags"/> are system-set key-value tags, settable only here (not from the UI) and immutable thereafter. <paramref name="createdBy"/> records who created the key; <c>null</c> for keys created without a user context (e.g. auto-generated), surfaced as "System" in the UI. <paramref name="ownerMemberKey"/> makes the key owner-scoped ("private") — bound to that team member and hidden from / immutable by other members; <c>null</c> = a normal team-wide key.</summary>
    Task<IApiKey> CreateKeyAsync(string teamKey, string name, AccessLevel accessLevel, string[] roles = null, string[] scopeOverrides = null, DateTime? expiryDate = null, IReadOnlyList<Tag> tags = null, string createdBy = null, string ownerMemberKey = null);

    /// <summary>Generates a new API key value for an existing key entry. Returns the entity with the raw key visible once.</summary>
    Task<IApiKey> RefreshKeyAsync(string teamKey, string key);

    /// <summary>
    /// Discards the stored secret so the raw key value can never be retrieved again. Verifies team ownership.
    /// </summary>
    /// <remarks>
    /// <b>This does not disable the key.</b> A locked key still authenticates — locking only makes the
    /// value unrecoverable, which is why <c>ApiKeyOptions.AutoLockKeys</c> can lock every key at creation
    /// without breaking anything.
    /// <para>
    /// To stop a key working, use <see cref="SetKeyDisabledAsync"/>, which is reversible and keeps the
    /// key's name, scopes, roles, tags and history. Delete only when the key should cease to exist.
    /// </para>
    /// </remarks>
    Task LockKeyAsync(string teamKey, string key);

    /// <summary>
    /// Stops a team key being usable, or makes it usable again, without losing its name, scopes, roles,
    /// tags or audit trail.
    /// </summary>
    /// <remarks>
    /// The reversible alternative to deletion, for the ordinary operational cases: a key suspected of
    /// leaking, a partner integration paused, a key parked while an incident is investigated.
    /// <para>
    /// <b>Not the same as locking.</b> Locking discards the stored secret so its raw value cannot be
    /// retrieved again; a locked key still authenticates. Disabling stops it working.
    /// </para>
    /// <para>
    /// <b>Refreshing does not enable a disabled key.</b> Minting a new secret is not a decision to trust
    /// the key again.
    /// </para>
    /// </remarks>
    Task SetKeyDisabledAsync(string teamKey, string key, bool disabled, string actor = null);

    /// <summary>Deletes an API key. Verifies team ownership.</summary>
    Task DeleteKeyAsync(string teamKey, string key);

    /// <summary>
    /// Sets the <c>ScopeOverrides</c> array on an existing team API key. Verifies team ownership.
    /// Pass <c>null</c> or an empty array to clear all overrides.
    /// </summary>
    Task SetScopeOverridesAsync(string teamKey, string key, string[] scopes);

    /// <summary>
    /// Sets the <c>Roles</c> (tenant roles) array on an existing team API key. Verifies team ownership.
    /// Pass <c>null</c> or an empty array to clear all roles.
    /// </summary>
    Task SetRolesAsync(string teamKey, string key, string[] roles);

    /// <summary>Returns all system-level API keys (not bound to a team).</summary>
    IAsyncEnumerable<IApiKey> GetSystemKeysAsync();

    /// <summary>Creates a new system-level API key with the specified explicit scope set.</summary>
    /// <param name="name">Human-readable name for the key.</param>
    /// <param name="scopes">Explicit scopes granted to this key. Not resolved through AccessLevel/roles.</param>
    /// <param name="expiryDate">Optional expiry date.</param>
    /// <param name="createdBy">Identity of the user creating the key (for audit).</param>
    Task<IApiKey> CreateSystemKeyAsync(string name, string[] scopes, DateTime? expiryDate = null, string createdBy = null);

    /// <summary>
    /// Creates a personal system-level API key, owned by <paramref name="ownerUserKey"/>: hidden from others
    /// in the UI, manageable only by its owner (or an <see cref="SystemApiKeyScopes.Oversee"/> holder), and
    /// refused at authentication once its owner no longer exists or is disabled.
    /// </summary>
    /// <param name="name">Human-readable name for the key.</param>
    /// <param name="scopes">Explicit scopes granted to this key. Not resolved through AccessLevel/roles.</param>
    /// <param name="ownerUserKey">The owning user's key, stored in <see cref="IApiKey.OwnerMemberKey"/>.</param>
    /// <param name="expiryDate">Optional expiry date.</param>
    /// <param name="createdBy">Identity of the user creating the key (for audit).</param>
    /// <remarks>
    /// <b>A default implementation that throws, so a host's own key store keeps compiling</b> after the 4.1
    /// upgrade. Such a store supports every other operation as before; implement this to support personal
    /// system keys, and keep <see cref="IApiKey.OwnerMemberKey"/> across <see cref="RefreshSystemKeyAsync"/>.
    /// </remarks>
    Task<IApiKey> CreatePersonalSystemKeyAsync(string name, string[] scopes, string ownerUserKey, DateTime? expiryDate = null, string createdBy = null)
        => throw new NotSupportedException("This API key store does not support personal system keys. Implement IApiKeyAdministrationService.CreatePersonalSystemKeyAsync, or set ApiKeyOptions.SystemKeys to ApiKeyOwnership.Shared.");

    /// <summary>Regenerates a system key's raw value. Returns the entity with the raw key visible once.</summary>
    Task<IApiKey> RefreshSystemKeyAsync(string key);

    /// <summary>
    /// Discards the stored secret of a system API key so its raw value can never be retrieved again.
    /// </summary>
    /// <remarks><b>This does not disable the key</b> — see <see cref="LockKeyAsync"/>.</remarks>
    Task LockSystemKeyAsync(string key);

    /// <inheritdoc cref="SetKeyDisabledAsync"/>
    Task SetSystemKeyDisabledAsync(string key, bool disabled, string actor = null);

    /// <summary>Deletes a system API key.</summary>
    Task DeleteSystemKeyAsync(string key);
}
