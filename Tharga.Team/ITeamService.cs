using System.ComponentModel;

namespace Tharga.Team;

/// <summary>
/// The team store — <b>the contract a host implements, not the one a component injects.</b>
/// </summary>
/// <remarks>
/// <b>Deliberately unchecked.</b> Framework code reads through this while constructing the very claims
/// that would authorize the read — <c>TeamMembershipClaimsBuilder</c> and
/// <c>TeamClaimsAuthenticationStateProvider</c> both resolve a member while building the principal — so
/// gating it would be circular and break sign-in. Authorization is applied by the decorators and gated
/// services layered over it, not here.
/// <para>
/// <b>A component, controller or MCP provider injecting this bypasses authorization entirely.</b> That is
/// not hypothetical: it is how <c>team:read</c> came to be registered, documented, granted — and checked
/// by nothing. Reaching around the gate was the path of least resistance and nothing said otherwise.
/// </para>
/// <para>Inject one of these instead:</para>
/// <list type="table">
///   <item><term><see cref="ITeamManagementService"/></term><description>One team: its details, roster, members, and every mutation.</description></item>
///   <item><term><see cref="ITeamDirectoryService"/></term><description>The caller's own teams, filtered by what each membership grants.</description></item>
///   <item><term><see cref="ITeamOversightService"/></term><description>Every team, regardless of membership. Requires <c>teams:read</c>.</description></item>
///   <item><term><see cref="ITeamInvitationService"/></term><description>Resolving an invite code, authorized by the code itself.</description></item>
///   <item><term><see cref="ITeamLifecycleService"/></term><description>Creating a team.</description></item>
/// </list>
/// <para>
/// Hidden from IntelliSense to prevent the honest mistake. It cannot prevent a deliberate or copy-pasted
/// one — an architecture test covers this repo, and a Roslyn analyzer is the only thing that would reach
/// consumer projects.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ITeamService
{
    event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent;
    event EventHandler<SelectTeamEventArgs> SelectTeamEvent;

    IAsyncEnumerable<ITeam> GetTeamsAsync();
    IAsyncEnumerable<ITeam<TMember>> GetTeamsAsync<TMember>() where TMember : ITeamMember;

    /// <summary>
    /// Every team, regardless of membership. Requires the <see cref="SystemTeamScopes.Read"/> system scope.
    /// </summary>
    /// <remarks>
    /// Discovery only — the returned teams carry no implied access. Acting inside a team the caller is not
    /// a member of still depends on that team's consent. Use <see cref="GetTeamsAsync()"/> for the caller's
    /// own teams; this method is for oversight surfaces (support, administration).
    /// </remarks>
    IAsyncEnumerable<ITeam> GetAllTeamsAsync();

    /// <inheritdoc cref="GetAllTeamsAsync()"/>
    IAsyncEnumerable<ITeam<TMember>> GetAllTeamsAsync<TMember>() where TMember : ITeamMember;
    Task<ITeam<TMember>> GetTeamAsync<TMember>(string teamKey) where TMember : ITeamMember;

    /// <summary>
    /// A team by key, regardless of the caller's membership — a non-generic exact read for call sites with
    /// no <c>TMember</c> to hand (e.g. the audit decorator capturing a "before" value for a consent change
    /// made by a non-member acting through consent). Returns null when the team does not exist.
    /// </summary>
    Task<ITeam> GetTeamByKeyAsync(string teamKey);

    Task<ITeam> CreateTeamAsync(string name = null);
    Task RenameTeamAsync<TMember>(string teamKey, string name) where TMember : ITeamMember;
    Task DeleteTeamAsync<TMember>(string teamKey) where TMember : ITeamMember;

    /// <summary>Restores a soft-deleted team.</summary>
    /// <remarks>
    /// A default interface method, so an existing implementation of this contract keeps compiling. The
    /// default throws rather than no-opping: reporting success for a restore that did not happen leaves an
    /// operator believing a team is back when it is still invisible.
    /// </remarks>
    Task RestoreTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(RestoreTeamAsync)}.");

    /// <summary>Permanently removes a team and its storage. Irreversible.</summary>
    /// <remarks>
    /// A default interface method for the same reason as <see cref="RestoreTeamAsync{TMember}"/>, and it
    /// throws for a stronger one: a purge that silently did nothing would leave storage the operator
    /// believes is gone.
    /// </remarks>
    Task PurgeTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(PurgeTeamAsync)}.");
    Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey);
    IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey);
    Task AddMemberAsync(string teamKey, InviteUserModel model);
    Task RemoveMemberAsync(string teamKey, string userKey);
    Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel);

    /// <inheritdoc cref="ITeamManagementService.SetMemberSuspendedAsync"/>
    Task SetMemberSuspendedAsync(string teamKey, string userKey, bool suspended)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetMemberSuspendedAsync)}.");
    Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles);
    Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides);
    Task SetMemberNameAsync(string teamKey, string userKey, string name);
    Task SetInvitationResponseAsync(string teamKey, string userKey, string inviteCode, bool accept);
    Task SetMemberLastSeenAsync(string teamKey);
    Task TransferOwnershipAsync<TMember>(string teamKey, string newOwnerUserKey) where TMember : ITeamMember;

    /// <summary>
    /// Makes an existing member the <b>sole owner</b> of the team, demoting every other owner to
    /// <see cref="AccessLevel.Administrator"/>. Requires the <see cref="SystemTeamScopes.SetOwner"/> system
    /// scope. Returns the user keys of the owners demoted, empty when nothing changed.
    /// </summary>
    /// <remarks>
    /// Refuses only when the candidate is not already a member — see <see cref="TeamOwnership"/> for why
    /// that one condition is load-bearing and why the current owner count deliberately is not. Serves three
    /// states: a team with several owners (a legacy sync), a team whose owner cannot hand over themselves,
    /// and a team with no owner at all.
    /// <para>
    /// Distinct from <see cref="TransferOwnershipAsync{TMember}"/>, which requires the caller to <i>be</i>
    /// the owner. That is the in-team path and stays as it is; this is the operator path.
    /// </para>
    /// </remarks>
    Task<SetOwnerResult> SetOwnerAsync<TMember>(string teamKey, string newOwnerUserKey) where TMember : ITeamMember;
    Task SetTeamConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null);
    IAsyncEnumerable<ITeam> GetConsentedTeamsAsync(string[] userRoles);
    Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey);
    Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles);

    /// <summary>
    /// Removes the user from every team they appear in, regardless of membership state. Backs user
    /// deletion (<see cref="IUserManagementService.DeleteUserAsync"/>); requires the
    /// <see cref="SystemUserScopes.Manage"/> system scope. Returns the number of teams the user was
    /// removed from.
    /// </summary>
    Task<int> RemoveUserFromAllTeamsAsync(string userKey);

    /// <summary>
    /// The teams where this user holds exactly <paramref name="accessLevel"/>. Requires the
    /// <see cref="SystemUserScopes.Manage"/> system scope.
    /// </summary>
    /// <remarks>
    /// The driver is user deletion: asking for <see cref="AccessLevel.Owner"/> answers "which teams will
    /// this delete strand?", so the admin can transfer ownership <i>before</i> deleting rather than
    /// being told afterwards that something is now unrecoverable.
    /// <para>
    /// <b>Exact match, not minimum.</b> "Teams they own" is the question; a caller wanting a threshold
    /// can ask more than once. A minimum-level overload would make the common case ambiguous at the call
    /// site, where <c>Owner</c> would silently also mean every level above it — of which there are none,
    /// making the parameter read as if it did something it does not.
    /// </para>
    /// <para>
    /// Gated on <c>users:manage</c> rather than <c>teams:read</c>, deliberately. The caller of this
    /// already holds the right to remove the user from every one of these teams, so learning which they
    /// are is strictly less than they can already do — and gating it on a scope they may not hold would
    /// hide the warning from exactly the person about to cause the damage.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelAsync(string userKey, AccessLevel accessLevel);

    /// <summary>
    /// Sets the team's icon from raw image bytes: stores them via the registered <see cref="IIconStore"/>,
    /// persists the reference on the team, and deletes any previously-stored icon. Requires a registered
    /// icon store. Gated by <c>team:manage</c>.
    /// </summary>
    Task SetTeamIconAsync(string teamKey, byte[] data, string contentType);

    /// <summary>
    /// Clears the team's icon and deletes the stored bytes. Gated by <c>team:manage</c>.
    /// </summary>
    Task ClearTeamIconAsync(string teamKey);
}