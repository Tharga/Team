using System.Security.Claims;

namespace Tharga.Team.Service;

/// <summary>
/// Decorator over <see cref="IUserService"/> that drops the cached copy of a user after any call that
/// changes them — whoever implemented the write.
/// </summary>
/// <remarks>
/// <c>UserServiceBase</c> caches resolved users and invalidates in the paths it owns. A host overriding
/// one of those to supply persistence <i>replaces</i> the path that invalidates, so the write commits
/// and every later read is served stale. That is the toolkit dropping a responsibility it owns, not a
/// host concern, so the fix belongs here rather than in a note telling every host to remember.
/// <para>
/// <b>Why a decorator and not a template method.</b> The obvious fix is to make the mutating members
/// non-virtual and have them call a <c>protected abstract</c> hook, which would make it impossible to
/// get wrong. That is a <b>breaking change</b> — it stops every existing override compiling — so it is
/// 4.0 work. A decorator invalidates regardless of who implemented the member, needs no consumer
/// change, and can ship in a minor.
/// </para>
/// <para>
/// <b><see cref="IUserService.SetUserLastSeenAsync"/> is deliberately not invalidated.</b> It runs on
/// every authenticated resolve (throttled), so invalidating there would empty the cache continuously
/// and defeat the thing this class exists to keep correct. A cached <see cref="IUser.LastSeen"/> is
/// therefore up to one resolve stale, which is the behaviour that already shipped.
/// </para>
/// <para>
/// Invalidation runs only after the inner call returns. A throwing write has changed nothing, and
/// dropping a valid entry because a write failed would trade a stale read for a needless one.
/// </para>
/// </remarks>
public sealed class CacheInvalidatingUserServiceDecorator : IUserService
{
    private readonly IUserService _inner;
    private readonly IUserCacheInvalidator _invalidator;

    /// <param name="inner">The store to delegate to.</param>
    /// <remarks>
    /// A store that does not implement <see cref="IUserCacheInvalidator"/> has no cache to drop, so this
    /// decorator becomes a pass-through rather than an error.
    /// </remarks>
    public CacheInvalidatingUserServiceDecorator(IUserService inner)
    {
        _inner = inner;
        _invalidator = inner as IUserCacheInvalidator;
    }

    // The async form, because the cache behind it may be remote (see ITeamCache) and the synchronous member
    // could then only be served by blocking a request thread.
    private Task InvalidateAsync(string userKey) => _invalidator?.InvalidateUserByKeyAsync(userKey) ?? Task.CompletedTask;

    public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => _inner.GetCurrentUserAsync(claimsPrincipal);

    public IAsyncEnumerable<IUser> GetAsync() => _inner.GetAsync();

    public Task<IReadOnlyList<IUser>> GetTeamMemberUsersAsync() => _inner.GetTeamMemberUsersAsync();

    public Task<IUser> GetUserByKeyAsync(string userKey) => _inner.GetUserByKeyAsync(userKey);

    public Task SetUserLastSeenAsync(string userKey, DateTime lastSeen) => _inner.SetUserLastSeenAsync(userKey, lastSeen);

    public async Task SeedUserNameAsync(string userKey, string name)
    {
        await _inner.SeedUserNameAsync(userKey, name);
        await InvalidateAsync(userKey);
    }

    public async Task SetUserNameAsync(string userKey, string name)
    {
        await _inner.SetUserNameAsync(userKey, name);
        await InvalidateAsync(userKey);
    }

    public async Task SetUserEmailAsync(string userKey, string email)
    {
        await _inner.SetUserEmailAsync(userKey, email);
        await InvalidateAsync(userKey);
    }

    public async Task SetUserDirectoryIdAsync(string userKey, string directoryId)
    {
        await _inner.SetUserDirectoryIdAsync(userKey, directoryId);
        await InvalidateAsync(userKey);
    }

    public async Task SetUserIconAsync(string userKey, byte[] data, string contentType)
    {
        await _inner.SetUserIconAsync(userKey, data, contentType);
        await InvalidateAsync(userKey);
    }

    public async Task ClearUserIconAsync(string userKey)
    {
        await _inner.ClearUserIconAsync(userKey);
        await InvalidateAsync(userKey);
    }

    public async Task SetUserDisabledAsync(string userKey, DateTime? disabledAt, string disabledBy)
    {
        await _inner.SetUserDisabledAsync(userKey, disabledAt, disabledBy);
        await InvalidateAsync(userKey);
    }

    public async Task DeleteUserAsync(string userKey)
    {
        await _inner.DeleteUserAsync(userKey);
        await InvalidateAsync(userKey);
    }

    /// <remarks>
    /// The self-service icon members take no key, so the caller is resolved first to learn which entry to
    /// drop. That read is a cache hit in the case that matters — the caller is signed in, which is what
    /// put them in the cache — so it costs a dictionary lookup, not a round trip.
    /// </remarks>
    public async Task SetOwnIconAsync(byte[] data, string contentType)
    {
        var userKey = (await _inner.GetCurrentUserAsync())?.Key;
        await _inner.SetOwnIconAsync(data, contentType);
        await InvalidateAsync(userKey);
    }

    /// <inheritdoc cref="SetOwnIconAsync" />
    public async Task ClearOwnIconAsync()
    {
        var userKey = (await _inner.GetCurrentUserAsync())?.Key;
        await _inner.ClearOwnIconAsync();
        await InvalidateAsync(userKey);
    }
}
