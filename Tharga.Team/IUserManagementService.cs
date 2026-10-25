namespace Tharga.Team;

/// <summary>
/// User administration operations: directory verification and user deletion. Authorization is enforced
/// in the service layer by an authorization decorator; the <c>[RequireScope]</c> attributes here document
/// the scope each operation requires. All operations require the <see cref="SystemUserScopes.Manage"/>
/// system scope. Directory-backed operations require a registered <see cref="IUserDirectoryService"/>.
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Verify a local user against the external directory. When the user resolves via email fallback,
    /// the found directory id is persisted on the user (relink).
    /// </summary>
    [RequireScope(SystemUserScopes.Manage)]
    Task<DirectoryVerificationResult> VerifyUserAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify all local users against the external directory, streamed as results arrive.
    /// </summary>
    [RequireScope(SystemUserScopes.Manage)]
    IAsyncEnumerable<UserVerificationResult> VerifyAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a user: removes the user from every team and deletes the user record (audited).
    /// With <paramref name="deleteFromDirectory"/> the user is also deleted from the external directory;
    /// a directory failure does not roll back the local delete — it is reported on the result.
    /// </summary>
    [RequireScope(SystemUserScopes.Manage)]
    Task<UserDeleteResult> DeleteUserAsync(string userKey, bool deleteFromDirectory = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables the user, or enables them again. A disabled user keeps their record, their memberships
    /// and their history — the reversible alternative to <see cref="DeleteUserAsync"/>.
    /// </summary>
    /// <remarks>
    /// <b>A caller cannot disable themselves.</b> Enforced here rather than only hidden in the UI: an
    /// administrator who locks themselves out needs a second administrator to undo it, and refusing the
    /// self-case also guarantees somebody is left holding <see cref="SystemUserScopes.Manage"/>.
    /// <para>
    /// <b>This is not <see cref="DirectoryUserStatus.Disabled"/></b>, which means disabled in the external
    /// directory. This one blocks the user from this application only, and the two are shown separately.
    /// </para>
    /// <para>
    /// <b>It does not cascade to the user's API keys.</b> A key is not a session — it is an independent
    /// credential with its own lifecycle, and retiring a person's integrations is a separate deliberate
    /// act (which is also what keeps each one reversible on its own).
    /// </para>
    /// A signed-in user is not evicted instantly; they are signed out within
    /// <c>ClaimRevalidationOptions.Interval</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The caller is the user being disabled.</exception>
    [RequireScope(SystemUserScopes.Manage)]
    Task SetUserDisabledAsync(string userKey, bool disabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a user's display name, and, when <paramref name="writeToDirectory"/> is true, write it to the
    /// external directory too.
    /// </summary>
    /// <param name="userKey">The user to rename.</param>
    /// <param name="name">The new name. Null or empty clears the local name, and cannot be written to the directory.</param>
    /// <param name="writeToDirectory">
    /// Whether to write the name to the directory as well. The caller decides on every rename;
    /// <c>o.Blazor.WriteNameToDirectory</c> only allows it.
    /// </param>
    /// <param name="cancellationToken">Cancels the directory write.</param>
    /// <remarks>
    /// A write to the directory that cannot be honoured is refused before anything changes: the host has not
    /// allowed it, no directory is registered, the user is not linked to a directory account, or the name is a
    /// placeholder (<see cref="DirectoryNames.IsPlaceholder"/>). Once the local write has happened, the directory
    /// write is best-effort and its outcome is reported on the result rather than rolled back into the local one:
    /// they fail independently, and coupling them would let a directory outage block renaming a user here.
    /// <para>
    /// Administrative rename only. The self-service path (<c>IUserService.SetUserNameAsync</c>) stays
    /// local deliberately — a user editing their own display name in this application should not silently
    /// rewrite the organization's directory.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The user was not found, or <paramref name="writeToDirectory"/> is true and the write cannot be honoured.
    /// </exception>
    /// <exception cref="NotSupportedException"><paramref name="writeToDirectory"/> is true and no directory is registered.</exception>
    [RequireScope(SystemUserScopes.Manage)]
    Task<UserNameChangeResult> SetUserNameAsync(string userKey, string name, bool writeToDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a user's email address. It stays local: a directory-backed user's address is usually owned by the
    /// organisation, so it is never written back to the directory.
    /// </summary>
    /// <remarks>
    /// The address is trimmed and must be a single, plain address. <b>Nothing is sent to it</b>, and it is not
    /// checked against other users: more than one user may hold the same address. It is what the user is
    /// mailed at, what an invitation to them matches, and what their avatar and fallback name come from,
    /// so the change records the address it replaced.
    /// <para>
    /// A host that refreshes the profile from trusted sign-ins (<c>UserServiceBase.ShouldRefreshProfile</c>)
    /// will have the address set back to the one the identity provider sends the next time they sign in.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The address is empty or not a single plain address.</exception>
    /// <exception cref="InvalidOperationException">The user was not found.</exception>
    /// <exception cref="NotSupportedException">The host's user service does not implement it.</exception>
    [RequireScope(SystemUserScopes.Manage)]
    Task<UserEmailChangeResult> SetUserEmailAsync(string userKey, string email, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(SetUserEmailAsync)}.");

    /// <summary>
    /// Replace the user's local name with the display name the directory holds for them, and return it.
    /// </summary>
    /// <remarks>
    /// The directory is read here rather than the name being passed in, so the operation can only ever take
    /// what the directory actually says. Refused when the directory name is a placeholder
    /// (<see cref="DirectoryNames.IsPlaceholder"/>): taking "unknown" would overwrite a real name with none.
    /// Local only — the name came from the directory, so nothing is written back.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The user is not found in the directory, or the directory holds only a placeholder name.
    /// </exception>
    /// <exception cref="NotSupportedException">The host's user management service does not implement it.</exception>
    [RequireScope(SystemUserScopes.Manage)]
    Task<string> UseDirectoryNameAsync(string userKey, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(UseDirectoryNameAsync)}.");

    /// <summary>
    /// The teams this user owns — the teams that deleting them would leave with no owner.
    /// </summary>
    /// <remarks>
    /// Meant to be asked <b>before</b> confirming a delete, so the operator can transfer ownership
    /// instead of learning afterwards that a team is unrecoverable: <c>TransferOwnershipAsync</c>
    /// requires the caller to be the owner, so once the owner is gone only a holder of
    /// <see cref="SystemTeamScopes.SetOwner"/> can repair it.
    /// <para>
    /// On <see cref="IUserManagementService"/> rather than <c>ITeamService</c> deliberately. The question
    /// is "what will deleting this user break", which is user administration; and it keeps the delete
    /// dialog off the internal team contract, which no component should inject.
    /// </para>
    /// </remarks>
    [RequireScope(SystemUserScopes.Manage)]
    Task<IReadOnlyList<ITeam>> GetOwnedTeamsAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// List directory users that have no matching local user (matched by directory id, falling back to
    /// email), streamed as directory pages arrive.
    /// </summary>
    [RequireScope(SystemUserScopes.Manage)]
    IAsyncEnumerable<DirectoryUser> GetDirectoryOnlyUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How each of the given users signs in, read from the directory in one batch. One entry per user that
    /// is linked to a directory account the directory still knows; unknown and unlinked users are left out.
    /// </summary>
    /// <remarks>
    /// Requires a registered <see cref="IUserIdentityDirectory"/>; without one the result is empty. Meant
    /// for a list surface reading the rows it shows, so pass a page of keys rather than every user.
    /// <para>
    /// The default implementation returns nothing, so a host's own implementation of this interface keeps
    /// compiling; such a host shows no sign-in methods until it implements this.
    /// </para>
    /// </remarks>
    [RequireScope(SystemUserScopes.Manage)]
    Task<UserSignInIdentities[]> GetSignInIdentitiesAsync(IEnumerable<string> userKeys, CancellationToken cancellationToken = default)
        => Task.FromResult(Array.Empty<UserSignInIdentities>());
}
