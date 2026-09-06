namespace Tharga.Team;

/// <summary>
/// The caller's own teams — <b>the interface a component, controller or MCP provider should inject</b> to
/// list them. Scope-<i>filtered</i> rather than scope-gated: each team is included only if the caller's
/// membership in that team grants <c>team:read</c>.
/// </summary>
/// <remarks>
/// Separate from <see cref="ITeamManagementService"/> because that interface is wholly team-bound — every
/// operation names a team in its first argument, which is what lets one registration authorize all of
/// them. This one names no team, so it cannot be gated the same way and does not belong there.
/// <para>
/// It cannot use <c>[RequireScope]</c> for the same reason, and a principal carries scope claims only for
/// the *selected* team, so there is nothing in the claims to check the others against. The scopes are
/// recomputed per team from the caller's membership instead — the same inputs the claims builder uses.
/// </para>
/// </remarks>
public interface ITeamDirectoryService
{
    /// <summary>
    /// The caller's teams, omitting any where their membership does not grant <c>team:read</c>. A team is
    /// omitted whole rather than returned without its roster: the scope covers "team details and members"
    /// together, so a half-visible team would be a state nothing else in the model has.
    /// </summary>
    IAsyncEnumerable<ITeam<TMember>> GetTeamsAsync<TMember>() where TMember : ITeamMember;

    /// <summary>
    /// The same teams without their rosters, for a caller that does not know the host's member type.
    /// </summary>
    /// <remarks>
    /// Filtered identically — it runs the generic overload and drops the rosters, rather than reproducing
    /// the per-team scope recomputation. Two copies of that rule would be two chances for it to drift.
    /// </remarks>
    IAsyncEnumerable<ITeam> GetTeamsAsync();

    /// <summary>
    /// Whether the caller's own membership in this team is suspended. A suspended member still sees the
    /// team here and in the selector — they simply hold no scopes in it.
    /// </summary>
    /// <remarks>
    /// <b>Ungated on purpose, and it has to be.</b> A suspended member holds no team scopes at all, so any
    /// scope-checked read would refuse them — and then nothing could tell them why their team stopped
    /// working. This asks only about the caller's own membership, which is not somebody else's
    /// information to protect.
    /// <para>
    /// Suspension is invisible to <see cref="GetTeamsAsync()"/>'s <c>team:read</c> filter by design: that
    /// filter recomputes scopes from access level, roles and overrides, none of which suspension touches.
    /// The team therefore stays listed with no special case, which is exactly the intent.
    /// </para>
    /// </remarks>
    Task<bool> IsSuspendedAsync(string teamKey);

    /// <summary>
    /// Leaves the team. Refuses the Owner, who must transfer ownership first, and the last administrator
    /// of a team with no owner.
    /// </summary>
    /// <remarks>
    /// <b>The one mutation on this interface, and it belongs here rather than on
    /// <see cref="ITeamManagementService"/>.</b> That interface is gated: <c>ScopeProxy</c> throws on an
    /// unattributed method, so every operation there must name a scope. Leaving can name none —
    /// <c>member:manage</c>, which used to authorize it as a self-removal, is registered at
    /// <see cref="AccessLevel.Administrator"/> and so is held by nobody who most needs to leave, and a
    /// suspended member holds no scope whatsoever.
    /// <para>
    /// So it sits beside <see cref="IsSuspendedAsync"/>, ungated for the same reason: both concern only
    /// the caller's own membership, which is not somebody else's information to protect. The check is the
    /// signature — there is no user key to point at anyone else. The refusals are enforced by the service,
    /// not merely hidden in the UI.
    /// </para>
    /// <para>
    /// Removing <i>another</i> member remains <c>ITeamManagementService.RemoveMemberAsync</c> and still
    /// requires <c>member:manage</c>.
    /// </para>
    /// </remarks>
    Task LeaveTeamAsync(string teamKey);
}
