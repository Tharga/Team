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
    private readonly ILogger<UserServiceBase> _linkLogger;

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
        _linkLogger = logger;
    }

    protected abstract Task<TUserEntity> CreateUserEntityAsync(ClaimsPrincipal claimsPrincipal, string identity);

    protected override async Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal)
    {
        var identity = ResolveUserIdentity(claimsPrincipal);

        var user = await _userRepository.GetAsync(identity);
        if (user != null) return user;

        user = await LinkToExistingUserAsync(claimsPrincipal, identity);
        if (user != null) return user;

        var candidate = await CreateUserEntityAsync(claimsPrincipal, identity);
        if (await _userRepository.TryAddAsync(candidate))
        {
            // Only here. The path below lost the race, so another caller created the record and
            // announcing a second creation would put two entries in the audit log for one user.
            RaiseUserCreated(candidate, claimsPrincipal);

            return candidate;
        }

        // Lost the race against a concurrent first-time login for the same identity.
        // The unique Identity index on UserRepositoryCollection guarantees only one wins;
        // re-read and return the winner. Issue Tharga/Team#65. The insert is not reported as an error because the
        // loss is expected (Tharga/Team#457); a write that failed for another reason leaves no winner to read, which is thrown.
        return await _userRepository.GetAsync(identity)
            ?? throw new InvalidOperationException($"The user '{identity}' could not be added, and no other sign-in added it either.");
    }

    /// <summary>
    /// Whether somebody signing in with an identity no user holds is taken to be the one existing user with the same email
    /// address, instead of being given a new, empty user. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <b>Override it only when your identity provider verifies the email address it sends</b>, for example by returning
    /// whether the principal carries a verified-email claim. Linking on an address nobody checked lets anyone who can register
    /// that address at the provider sign in as the person who owns it here, with their teams. It is also off by default because
    /// it is a decision about who your users are: a person who really has two accounts is joined into one.
    /// <para>
    /// Only an address held by exactly one user links; two or more, or none, leave the sign-in to create a user as before.
    /// The existing user keeps their key, so their memberships and keys follow them, and from then on is found by the new
    /// identity, so the old one no longer signs in as them.
    /// </para>
    /// </remarks>
    protected virtual bool ShouldLinkToExistingUserByEmail(ClaimsPrincipal claimsPrincipal) => false;

    private async Task<TUserEntity> LinkToExistingUserAsync(ClaimsPrincipal claimsPrincipal, string identity)
    {
        if (!ShouldLinkToExistingUserByEmail(claimsPrincipal)) return null;

        var email = claimsPrincipal.GetEmail();
        if (string.IsNullOrWhiteSpace(email)) return null;

        try
        {
            var matches = await _userRepository.GetByEMailAsync(email);
            if (matches.Length != 1) return null;

            var existing = matches[0];
            await _userRepository.SetIdentityAsync(existing.Key, identity);
            await InvalidateUserCacheAsync(existing.Identity);
            await InvalidateUserCacheAsync(identity);

            return await _userRepository.GetAsync(identity);
        }
        catch (Exception e)
        {
            // Not linked, so the sign-in goes on to create a user as it always has rather than failing for want of a convenience.
            _linkLogger?.LogWarning(e, "Could not link the sign-in '{Identity}' to the existing user with its email address.", identity);

            return null;
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

    /// <remarks>Both identities are dropped from the cache: the user was cached under the old one, and a sign-in that raced the move may have cached the new one.</remarks>
    public override async Task SetUserIdentityAsync(string userKey, string identity)
    {
        if (string.IsNullOrEmpty(userKey)) return;
        if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("An identity is required.", nameof(identity));

        var user = await _userRepository.GetByKeyAsync(userKey);
        if (user == null) return;

        await _userRepository.SetIdentityAsync(userKey, identity);
        InvalidateUserCache(user.Identity);
        InvalidateUserCache(identity);
        await InvalidateUserByKeyAsync(userKey);
    }

    public override async Task SetUserEmailAsync(string userKey, string email)
    {
        if (string.IsNullOrEmpty(userKey)) return;

        var user = await _userRepository.GetByKeyAsync(userKey);
        if (user == null) return;

        await _userRepository.SetEMailAsync(userKey, email);
        InvalidateUserCache(user.Identity);
    }

    /// <inheritdoc />
    protected override async Task SetUserProfileAsync(string userKey, string name, string email)
    {
        if (string.IsNullOrEmpty(userKey)) return;

        if (name != null) await _userRepository.SetNameAsync(userKey, name);
        if (email != null) await _userRepository.SetEMailAsync(userKey, email);
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