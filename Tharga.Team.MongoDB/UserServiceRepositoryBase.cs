using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Security.Claims;
using Tharga.MongoDB;
using Tharga.Toolkit;

namespace Tharga.Team.MongoDB;

public abstract class UserServiceRepositoryBase<TUserEntity> : UserServiceBase
    where TUserEntity : EntityBase, IUser
{
    private readonly IUserRepository<TUserEntity> _userRepository;

    /// <param name="authenticationStateProvider">Resolves the calling principal when none is supplied.</param>
    /// <param name="userRepository">The user collection this service reads and writes.</param>
    /// <param name="logger">Optional. Used to report activity-stamping failures.</param>
    /// <param name="iconStore">Optional. Required only for user icons.</param>
    /// <param name="cache">
    /// Optional. <b>Forward it from your own service's constructor</b> when running more than one instance —
    /// left unforwarded, the resolved-user lookup falls back to a process-local cache that cannot see a user
    /// disabled through another instance. See <see cref="ITeamCache"/>.
    /// </param>
    protected UserServiceRepositoryBase(AuthenticationStateProvider authenticationStateProvider, IUserRepository<TUserEntity> userRepository, ILogger<UserServiceBase> logger = null, IIconStore iconStore = null, ITeamCache cache = null)
        : base(authenticationStateProvider, logger, iconStore, cache)
    {
        _userRepository = userRepository;
    }

    protected abstract Task<TUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity);

    protected override async Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal)
    {
        var identity = claimsPrincipal.GetIdentity().Identity;

        var user = await _userRepository.GetAsync(identity);
        if (user != null) return user;

        var candidate = await CreateUserEntityAsync(claimsPrincipal, identity);
        try
        {
            await _userRepository.AddAsync(candidate);

            // Only here. The duplicate-key path below lost the race, so another caller created the record and
            // announcing a second creation would put two entries in the audit log for one user.
            RaiseUserCreated(candidate, claimsPrincipal);

            return candidate;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Lost the race against a concurrent first-time login for the same identity.
            // The unique Identity index on UserRepositoryCollection guarantees only one wins;
            // re-read and return the winner. Issue Tharga/Team#65.
            return await _userRepository.GetAsync(identity);
        }
    }

    protected override IAsyncEnumerable<IUser> GetAllAsync()
    {
        return _userRepository.GetAsync();
    }

    public override async Task SeedUserNameAsync(string userKey, string name)
    {
        if (string.IsNullOrEmpty(userKey)) return;

        var user = await _userRepository.GetByKeyAsync(userKey);
        if (user == null) return;
        if (!string.IsNullOrEmpty(user.Name)) return;

        await _userRepository.SetNameAsync(userKey, name);
        InvalidateUserCache(user.Identity);
    }

    public override async Task SetUserNameAsync(string userKey, string name)
    {
        if (string.IsNullOrEmpty(userKey)) return;

        var user = await _userRepository.GetByKeyAsync(userKey);
        if (user == null) return;

        await _userRepository.SetNameAsync(userKey, name);
        InvalidateUserCache(user.Identity);
    }

    public override async Task<IUser> GetUserByKeyAsync(string userKey)
    {
        if (string.IsNullOrEmpty(userKey)) return null;

        return await _userRepository.GetByKeyAsync(userKey);
    }

    public override Task SetUserLastSeenAsync(string userKey, DateTime lastSeen)
    {
        if (string.IsNullOrEmpty(userKey)) return Task.CompletedTask;

        return _userRepository.SetLastSeenAsync(userKey, lastSeen);
    }

    public override async Task SetUserDirectoryIdAsync(string userKey, string directoryId)
    {
        if (string.IsNullOrEmpty(userKey)) return;

        var user = await _userRepository.GetByKeyAsync(userKey);
        if (user == null) return;

        await _userRepository.SetDirectoryIdAsync(userKey, directoryId);
        InvalidateUserCache(user.Identity);
    }

    protected override Task SetUserIconReferenceAsync(string userKey, string reference)
    {
        return _userRepository.SetIconAsync(userKey, reference);
    }

    public override async Task DeleteUserAsync(string userKey)
    {
        if (string.IsNullOrEmpty(userKey)) return;

        var user = await _userRepository.GetByKeyAsync(userKey);
        if (user == null) return;

        await _userRepository.DeleteAsync(userKey);
        InvalidateUserCache(user.Identity);
    }
}