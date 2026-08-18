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
    /// Set a user's display name, and — when <c>o.Blazor.WriteNameToDirectory</c> is enabled and the user
    /// is linked to a directory account — write it back to the external directory too.
    /// </summary>
    /// <remarks>
    /// The local write always happens; a failure there throws. The directory write is best-effort and its
    /// outcome is reported on the result rather than rolled back into the local one: they fail
    /// independently, and coupling them would let a directory outage block renaming a user here.
    /// <para>
    /// Administrative rename only. The self-service path (<c>IUserService.SetUserNameAsync</c>) stays
    /// local deliberately — a user editing their own display name in this application should not silently
    /// rewrite the organization's directory.
    /// </para>
    /// </remarks>
    [RequireScope(SystemUserScopes.Manage)]
    Task<UserNameChangeResult> SetUserNameAsync(string userKey, string name, CancellationToken cancellationToken = default);

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
}
