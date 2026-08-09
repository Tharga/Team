using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Tharga.Toolkit;

namespace Tharga.Team;

public abstract class UserServiceBase : IUserService, IUserCacheInvalidator
{
    protected readonly AuthenticationStateProvider _authenticationStateProvider;

    // Throttles rather than caches: they record what this process has already done, so nothing about them is
    // wrong per-instance -- a second instance stamping LastSeen once per interval of its own is correct.
    private static readonly ConcurrentDictionary<string, DateTime> _lastSeenStamped = new();
    private static readonly ConcurrentDictionary<string, byte> _directoryIdBackfillAttempted = new();
    private readonly ILogger<UserServiceBase> _logger;
    private readonly IIconStore _iconStore;
    private readonly ITeamCache _cache;

    /// <param name="authenticationStateProvider">Resolves the calling principal when none is supplied.</param>
    /// <param name="logger">Optional. Used to report activity-stamping failures, which never fail a resolve.</param>
    /// <param name="iconStore">Optional. Required only for user icons; see <see cref="SetUserIconAsync"/>.</param>
    /// <param name="cache">
    /// Where resolved users are kept. Defaults to the process-local <see cref="InMemoryTeamCache"/>, which is
    /// correct for a single instance only — <b>forward this parameter from your own service's
    /// constructor</b> so a shared implementation can be registered, or a multi-instance deployment will not
    /// see a user disabled through another instance. See <see cref="ITeamCache"/>.
    /// </param>
    protected UserServiceBase(
        AuthenticationStateProvider authenticationStateProvider,
        ILogger<UserServiceBase> logger = null,
        IIconStore iconStore = null,
        ITeamCache cache = null)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _logger = logger;
        _iconStore = iconStore;
        _cache = cache ?? InMemoryTeamCache.Shared;
    }

    /// <summary>
    /// The cache this instance actually ended up with, so <see cref="TeamCacheWiring"/> can tell a forwarded
    /// <see cref="ITeamCache"/> from the process-local fallback. Internal: a diagnostic, not API.
    /// </summary>
    internal ITeamCache CacheInUse => _cache;

    /// <summary>
    /// How often (at most) <see cref="IUser.LastSeen"/> is written on resolve. Null disables stamping;
    /// <see cref="TimeSpan.Zero"/> stamps on every resolve. The throttle is per process, so a multi-instance
    /// deployment writes at most once per interval per instance.
    /// </summary>
    protected virtual TimeSpan? LastSeenStampInterval => TimeSpan.FromMinutes(15);

    /// <summary>
    /// The caller, either as supplied or resolved from the circuit. Null when there is no circuit to ask —
    /// an MCP request handler, a hosted service, a message handler — because nothing there can name a
    /// caller, and crashing is not a better answer than saying so.
    /// </summary>
    protected virtual async Task<ClaimsPrincipal> GetClaims(ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal ?? await CircuitPrincipal.GetUserOrNullAsync(_authenticationStateProvider);
    }

    /// <summary>
    /// Raised when a user record is created as a side effect of someone signing in for the first time.
    /// </summary>
    /// <remarks>
    /// <b>An event rather than a constructor dependency, on purpose.</b> The creation happens in the storage
    /// base (<c>Tharga.Team.MongoDB</c>), which cannot see the audit types - they live in
    /// <c>Tharga.Team.Service</c>. An optional constructor parameter would also be one more thing a host's own
    /// service must remember to forward, which is the hazard <c>TeamCacheWiringCheck</c> exists to catch.
    /// <para>
    /// <b>Not the same as an administrator creating a user.</b> That path is already audited by
    /// <c>AuditingUserManagementServiceDecorator</c>. This one has no actor but the new user themselves.
    /// </para>
    /// </remarks>
    public event EventHandler<UserCreatedEventArgs> UserCreatedEvent;

    /// <summary>
    /// Announces a first-sign-in creation. Call only after the record is durably stored, and <b>only</b> for
    /// the caller that actually created it - a store losing the insert race re-reads the winner and must not
    /// report a creation it did not perform.
    /// </summary>
    protected void RaiseUserCreated(IUser user, ClaimsPrincipal claimsPrincipal)
    {
        if (user == null) return;

        UserCreatedEvent?.Invoke(this, new UserCreatedEventArgs(user, claimsPrincipal));
    }

    protected abstract Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal);
    protected abstract IAsyncEnumerable<IUser> GetAllAsync();

    public async Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal)
    {
        claimsPrincipal = await GetClaims(claimsPrincipal);
        if (claimsPrincipal == null) return null;

        var identity = claimsPrincipal.GetIdentity().Identity;
        if (identity == null) return null;

        var cached = await _cache.GetUserAsync(identity);
        var user = cached.Value;
        if (!cached.Found)
        {
            user = await GetUserAsync(claimsPrincipal);
            await _cache.SetUserAsync(identity, user);
        }

        await TouchUserAsync(user, claimsPrincipal);

        return user;
    }

    private async Task TouchUserAsync(IUser user, ClaimsPrincipal claimsPrincipal)
    {
        if (user == null || string.IsNullOrEmpty(user.Key)) return;

        // Activity tracking must never break the resolve path (it runs inside the auth pipeline).
        try
        {
            await StampLastSeenAsync(user.Key);
            await BackfillDirectoryIdAsync(user, claimsPrincipal);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to stamp activity for user {UserKey}.", user.Key);
        }
    }

    private async Task StampLastSeenAsync(string userKey)
    {
        var interval = LastSeenStampInterval;
        if (interval == null) return;

        var now = DateTime.UtcNow;
        if (_lastSeenStamped.TryGetValue(userKey, out var stamped) && now - stamped < interval) return;

        _lastSeenStamped[userKey] = now;
        await SetUserLastSeenAsync(userKey, now);
    }

    private async Task BackfillDirectoryIdAsync(IUser user, ClaimsPrincipal claimsPrincipal)
    {
        if (!string.IsNullOrEmpty(user.DirectoryId)) return;

        // One attempt per user per process: if the store does not persist DirectoryId the value stays
        // null, and retrying every resolve would invalidate the user cache on each request.
        if (!_directoryIdBackfillAttempted.TryAdd(user.Key, 0)) return;

        var directoryId = claimsPrincipal.GetDirectoryId();
        if (string.IsNullOrEmpty(directoryId)) return;

        await SetUserDirectoryIdAsync(user.Key, directoryId);
    }

    public virtual IAsyncEnumerable<IUser> GetAsync()
    {
        return GetAllAsync();
    }

    public virtual Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;

    public virtual Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;

    public virtual async Task<IUser> GetUserByKeyAsync(string userKey)
    {
        if (string.IsNullOrEmpty(userKey)) return null;

        await foreach (var user in GetAllAsync())
        {
            if (user.Key == userKey) return user;
        }

        return null;
    }

    public virtual Task SetUserLastSeenAsync(string userKey, DateTime lastSeen) => Task.CompletedTask;

    public virtual Task SetUserDirectoryIdAsync(string userKey, string directoryId) => Task.CompletedTask;

    /// <summary>
    /// Backs <see cref="SetOwnIconAsync"/> / <see cref="ClearOwnIconAsync"/> — persists the icon reference
    /// (or null to clear) on the user document. Default no-op; stores that track <see cref="IUser.Icon"/>
    /// override it.
    /// </summary>
    protected virtual Task SetUserIconReferenceAsync(string userKey, string reference) => Task.CompletedTask;

    /// <summary>
    /// Refuses an icon upload the store cannot keep, <b>before</b> any bytes are written.
    /// </summary>
    /// <remarks>
    /// <see cref="SetUserIconReferenceAsync"/> is a no-op unless the entity declares
    /// <see cref="IUser.Icon"/>, so without this the upload stored a blob, silently discarded the
    /// reference, and reported success — leaving an orphan in the icon store and an unchanged avatar with
    /// nothing logged. Throwing here also matches <c>RequireIconStore</c>, which already names its own
    /// unmet prerequisite rather than doing nothing (Tharga/Team#160).
    /// </remarks>
    private static void RequireIconPersistence(IUser user)
    {
        if (IconCapability.CanPersistUserIcon(user.GetType())) return;

        throw new NotSupportedException(
            $"User icons require an '{nameof(IUser.Icon)}' property on the user entity, and " +
            $"'{user.GetType().Name}' does not declare one. Without it the reference cannot be persisted " +
            "and the upload would be discarded. Declare the property to opt in — see docs/articles/icons.md.");
    }

    public virtual async Task SetOwnIconAsync(byte[] data, string contentType)
    {
        var store = RequireIconStore();
        var user = await GetCurrentUserAsync();
        if (user == null) throw new UnauthorizedAccessException("Authentication required.");

        RequireIconPersistence(user);

        var previousReference = user.Icon;
        var reference = await store.SaveAsync(IconKind.User, user.Key, data, contentType);
        await SetUserIconReferenceAsync(user.Key, reference);

        if (!string.IsNullOrEmpty(previousReference))
            await store.DeleteAsync(previousReference);

        await _cache.RemoveUserAsync(user.Identity);
    }

    public virtual async Task ClearOwnIconAsync()
    {
        var store = RequireIconStore();
        var user = await GetCurrentUserAsync();
        if (user == null) throw new UnauthorizedAccessException("Authentication required.");

        var previousReference = user.Icon;
        if (string.IsNullOrEmpty(previousReference)) return;

        await SetUserIconReferenceAsync(user.Key, null);
        await store.DeleteAsync(previousReference);

        await _cache.RemoveUserAsync(user.Identity);
    }

    public virtual async Task SetUserIconAsync(string userKey, byte[] data, string contentType)
    {
        var store = RequireIconStore();
        var user = await GetUserByKeyAsync(userKey);
        if (user == null) throw new InvalidOperationException($"User '{userKey}' was not found.");

        RequireIconPersistence(user);

        var previousReference = user.Icon;
        var reference = await store.SaveAsync(IconKind.User, user.Key, data, contentType);
        await SetUserIconReferenceAsync(user.Key, reference);

        if (!string.IsNullOrEmpty(previousReference))
            await store.DeleteAsync(previousReference);

        await _cache.RemoveUserAsync(user.Identity);
    }

    public virtual async Task ClearUserIconAsync(string userKey)
    {
        var store = RequireIconStore();
        var user = await GetUserByKeyAsync(userKey);
        if (user == null) return;

        var previousReference = user.Icon;
        if (string.IsNullOrEmpty(previousReference)) return;

        await SetUserIconReferenceAsync(user.Key, null);
        await store.DeleteAsync(previousReference);

        await _cache.RemoveUserAsync(user.Identity);
    }

    private IIconStore RequireIconStore()
        => _iconStore ?? throw new NotSupportedException(
            "No IIconStore was supplied to this service. User icons require one, and there are two ways to " +
            "be missing it: (a) none is registered — the built-in MongoIconStore comes from " +
            "AddThargaTeamRepository, or supply your own via o.AddIconStore<T>(); or (b) it IS registered " +
            "but this service did not receive it — UserServiceRepositoryBase takes an optional " +
            "'IIconStore iconStore = null' constructor parameter, so a subclass that does not forward it " +
            "gets null here. See docs/articles/icons.md.");

    private Task<IUser> GetCurrentUserAsync() => GetCurrentUserAsync(null);

    /// <remarks>
    /// Throws rather than no-opping when unimplemented, exactly as <see cref="DeleteUserAsync"/> does:
    /// silently accepting a disable would report a containment that never happened.
    /// </remarks>
    public virtual Task SetUserDisabledAsync(string userKey, DateTime? disabledAt, string disabledBy)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetUserDisabledAsync)}. Implement it, and " +
            $"declare {nameof(IUser.DisabledAt)}/{nameof(IUser.DisabledBy)} on your user entity, to " +
            $"support disabling users (the '{SystemUserScopes.Manage}' system scope).");

    public virtual Task DeleteUserAsync(string userKey)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(DeleteUserAsync)}. Implement it to support " +
            $"user deletion (the '{SystemUserScopes.Manage}' system scope).");

    /// <summary>
    /// Drops the cached user for <paramref name="identity"/>. Retained for hosts that call it; the toolkit's own
    /// paths use <see cref="ITeamCache.RemoveUserAsync"/> directly.
    /// </summary>
    /// <remarks>
    /// Synchronous, so it waits on the cache. That is free for the built-in
    /// <see cref="InMemoryTeamCache"/> — every member completes synchronously — but a host that has
    /// registered a <b>remote</b> <see cref="ITeamCache"/> should prefer
    /// <see cref="InvalidateUserCacheAsync"/> rather than blocking a request thread on it.
    /// </remarks>
    protected void InvalidateUserCache(string identity) => InvalidateUserCacheAsync(identity).GetAwaiter().GetResult();

    /// <summary>Drops the cached user for <paramref name="identity"/>. A no-op when nothing is cached.</summary>
    protected Task InvalidateUserCacheAsync(string identity) => _cache.RemoveUserAsync(identity);

    /// <inheritdoc />
    /// <remarks>See <see cref="InvalidateUserCache"/> on why blocking here is safe for the built-in cache and not for a remote one.</remarks>
    public void InvalidateUserByKey(string userKey) => InvalidateUserByKeyAsync(userKey).GetAwaiter().GetResult();

    /// <inheritdoc />
    public Task InvalidateUserByKeyAsync(string userKey) => _cache.RemoveUserByKeyAsync(userKey);
}