using System.Security.Claims;

namespace Tharga.Team;

/// <summary>
/// The user store. Authorization is enforced in the service layer by
/// <c>AuthorizationUserServiceDecorator</c>: resolving the current user and the invitation-accept name
/// seeding are self-service; setting a display name is allowed on the caller's own record (otherwise
/// <c>users:manage</c>); the <c>[RequireScope]</c>-annotated members document the scope they require.
/// </summary>
public interface IUserService
{
    Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null);

    /// <summary>
    /// The user for <paramref name="claimsPrincipal"/>, resolved as <see cref="GetCurrentUserAsync(ClaimsPrincipal)"/>
    /// does, saying whether the resolve is the user being active.
    /// </summary>
    /// <param name="claimsPrincipal">The caller; null for the one on the current circuit.</param>
    /// <param name="recordActivity">
    /// Whether this resolve counts towards <see cref="IUser.LastSeen"/>. Pass <c>false</c> from code that reads the user
    /// on a schedule rather than because they did something, such as claim revalidation on an idle tab.
    /// </param>
    /// <remarks>
    /// Only the last-seen stamp depends on it; keeping the record right from the sign-in (the directory id, a trusted
    /// profile refresh) happens either way. The default forwards to <see cref="GetCurrentUserAsync(ClaimsPrincipal)"/>,
    /// so a host's own store keeps compiling and records whatever it recorded before.
    /// </remarks>
    Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal, bool recordActivity) => GetCurrentUserAsync(claimsPrincipal);

    /// <summary>All users. Cross-user enumeration — requires <see cref="SystemUserScopes.Manage"/>.</summary>
    [RequireScope(SystemUserScopes.Manage)]
    IAsyncEnumerable<IUser> GetAsync();

    /// <summary>
    /// The users who share at least one team with the caller, plus the caller themselves. Self-service:
    /// an authenticated caller is required but no scope, because the result is derived entirely from the
    /// caller's own team memberships and takes no argument that could widen it.
    /// </summary>
    /// <remarks>
    /// This is the identity source for the team member list when the caller lacks
    /// <see cref="SystemUserScopes.Manage"/>. A member row's email, display name and icon live on the user
    /// record rather than on <see cref="ITeamMember"/> — accepting an invitation clears the per-team name
    /// override and promotes it to <see cref="IUser.Name"/> — so without this projection a team owner would
    /// see their own accepted members as unidentified.
    /// The default implementation returns an empty list; <c>AuthorizationUserServiceDecorator</c> supplies
    /// the real projection, as it holds the undecorated store.
    /// </remarks>
    Task<IReadOnlyList<IUser>> GetTeamMemberUsersAsync() => Task.FromResult<IReadOnlyList<IUser>>([]);

    /// <summary>
    /// Sets the user's display name only if it is currently null/empty. Used by the
    /// invitation-accept flow to promote the admin-entered invitation name into the
    /// new user's identity without clobbering an IdP-provided name.
    /// </summary>
    Task SeedUserNameAsync(string userKey, string name);

    /// <summary>
    /// Always sets the user's display name. Used by the user self-edit flow where the
    /// caller has explicitly chosen a name for themselves.
    /// </summary>
    Task SetUserNameAsync(string userKey, string name);

    /// <summary>
    /// Sets the user's email address (administrative). Call through
    /// <see cref="IUserManagementService.SetUserEmailAsync"/>, which validates the address and audits the change;
    /// requires <see cref="SystemUserScopes.Manage"/>.
    /// </summary>
    /// <remarks>
    /// Throws rather than no-opping when unimplemented, as <see cref="SetUserDisabledAsync"/> does: silently
    /// skipping the change would report an address that was never set.
    /// </remarks>
    [RequireScope(SystemUserScopes.Manage)]
    Task SetUserEmailAsync(string userKey, string email)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetUserEmailAsync)}. Implement it to let an " +
            $"administrator set a user's email address (the '{SystemUserScopes.Manage}' system scope).");

    /// <summary>
    /// The user with the given key, or null. The default implementation scans <see cref="GetAsync"/>;
    /// storage-backed services override it with a direct read.
    /// </summary>
    [RequireScope(SystemUserScopes.Manage)]
    async Task<IUser> GetUserByKeyAsync(string userKey)
    {
        if (string.IsNullOrEmpty(userKey)) return null;

        await foreach (var user in GetAsync())
        {
            if (user.Key == userKey) return user;
        }

        return null;
    }

    /// <summary>
    /// Stamps when the user last made an authenticated request. The automatic throttled stamping is an
    /// internal self-call that bypasses the authorization decorator; calling this member from outside
    /// requires <see cref="SystemUserScopes.Manage"/>. The default is a no-op — stores that track
    /// <see cref="IUser.LastSeen"/> override it.
    /// </summary>
    [RequireScope(SystemUserScopes.Manage)]
    Task SetUserLastSeenAsync(string userKey, DateTime lastSeen) => Task.CompletedTask;

    /// <summary>
    /// Links the user to their external-directory id (<see cref="IUser.DirectoryId"/>). Called by the
    /// oid backfill (internal self-call) and by directory verification on an email-fallback match
    /// (relink). Default is a no-op — stores that track the directory id override it.
    /// </summary>
    [RequireScope(SystemUserScopes.Manage)]
    Task SetUserDirectoryIdAsync(string userKey, string directoryId) => Task.CompletedTask;

    /// <summary>
    /// Sets the current user's own icon from raw image bytes (self-service): stores them via the
    /// registered <see cref="IIconStore"/>, persists the reference on the user, and deletes any previous
    /// icon. Requires a registered icon store and an authenticated caller.
    /// </summary>
    Task SetOwnIconAsync(byte[] data, string contentType) => Task.CompletedTask;

    /// <summary>
    /// Clears the current user's own icon and deletes the stored bytes (self-service).
    /// </summary>
    Task ClearOwnIconAsync() => Task.CompletedTask;

    /// <summary>
    /// Sets a specific user's icon (administrative). The mechanism is the same as
    /// <see cref="SetOwnIconAsync"/> but targets <paramref name="userKey"/>; requires
    /// <see cref="SystemUserScopes.Manage"/>.
    /// </summary>
    [RequireScope(SystemUserScopes.Manage)]
    Task SetUserIconAsync(string userKey, byte[] data, string contentType) => Task.CompletedTask;

    /// <summary>Clears a specific user's icon (administrative). Requires <see cref="SystemUserScopes.Manage"/>.</summary>
    [RequireScope(SystemUserScopes.Manage)]
    Task ClearUserIconAsync(string userKey) => Task.CompletedTask;

    /// <summary>
    /// Deletes the user record from the store, with no team-membership cleanup — call through
    /// <see cref="IUserManagementService.DeleteUserAsync"/>, which removes team memberships and audits.
    /// </summary>
    /// <summary>
    /// Marks the user disabled, or clears the mark. A disabled user is refused at sign-in and evicted
    /// from a live session within the claim-revalidation interval.
    /// </summary>
    /// <remarks>
    /// Throws rather than no-opping when unimplemented, for the same reason
    /// <see cref="DeleteUserAsync"/> does: silently skipping a requested disable would hide the missing
    /// implementation behind an apparently successful containment.
    /// </remarks>
    [RequireScope(SystemUserScopes.Manage)]
    Task SetUserDisabledAsync(string userKey, DateTime? disabledAt, string disabledBy)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetUserDisabledAsync)}. Implement it, and " +
            $"declare {nameof(IUser.DisabledAt)}/{nameof(IUser.DisabledBy)} on your user entity, to " +
            $"support disabling users.");

    [RequireScope(SystemUserScopes.Manage)]
    Task DeleteUserAsync(string userKey)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(DeleteUserAsync)}. Implement it to support " +
            $"user deletion (the '{SystemUserScopes.Manage}' system scope).");
}