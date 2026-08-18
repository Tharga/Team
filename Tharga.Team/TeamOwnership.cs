namespace Tharga.Team;

/// <summary>
/// Decides who may be made the owner of a team, and which sitting owners that displaces.
/// </summary>
/// <remarks>
/// Pure and static so the rules are testable without a store. They are the whole safety argument for
/// <see cref="SystemTeamScopes.SetOwner"/>, so they should not live inside a service method where only an
/// integration test can reach them.
/// <para>
/// <b>A team has exactly one owner. That is the invariant every rule here serves.</b>
/// <c>SetMemberRoleAsync</c> refuses to grant or revoke <c>Owner</c> in either direction, so no ordinary
/// path can add a second owner or remove the last one. Setting an owner is the only operation that moves
/// the role, and it always leaves exactly one — the promotion is applied first and the displaced owners
/// demoted after, so the team is never momentarily ownerless.
/// </para>
/// <para>
/// Two states violate the invariant, and a team arrives at both from outside this class rather than
/// through it: <c>RemoveUserFromAllTeamsAsync</c> removes the owner along with everyone else, leaving
/// <b>none</b>; and a team synced from a legacy system whose model permits several owners arrives carrying
/// <b>more than one</b>. Setting an owner is the repair for both, which is why it does not require the team
/// to be in any particular state first.
/// </para>
/// </remarks>
public static class TeamOwnership
{
    /// <summary>
    /// Whether the team currently has no member at <see cref="AccessLevel.Owner"/>.
    /// </summary>
    /// <remarks>
    /// A null or empty roster counts as ownerless. Setting an owner still refuses on one, because there is
    /// nobody to promote — see <see cref="CanSetOwner"/>.
    /// </remarks>
    public static bool IsOwnerless(IEnumerable<ITeamMember> members)
        => members?.Any(m => m != null && m.AccessLevel == AccessLevel.Owner) != true;

    /// <summary>
    /// Whether <paramref name="candidateUserKey"/> may be made the sole owner of this team.
    /// </summary>
    /// <remarks>
    /// One condition: the candidate must be an <b>existing member</b>. That is what keeps this a repair
    /// rather than a way to inject an outsider into a team the caller does not belong to — and the caller
    /// holds a system scope precisely because they are not a member.
    /// <para>
    /// Deliberately <b>not</b> conditioned on the current owner count. Refusing on a team that already has
    /// an owner would rule out the two cases this exists to serve: reducing a legacy team's several owners
    /// to one, and moving ownership when the sitting owner cannot do it themselves.
    /// </para>
    /// </remarks>
    public static bool CanSetOwner(IEnumerable<ITeamMember> members, string candidateUserKey)
    {
        if (string.IsNullOrEmpty(candidateUserKey)) return false;

        return members?.Any(m => m != null && m.Key == candidateUserKey) == true;
    }

    /// <summary>
    /// Whether <paramref name="userKey"/> is already the only owner, so setting them owner would change
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The caller is expected to be a repeated process — a sync reconciling teams from another system —
    /// so "already correct" is the common case rather than a mistake. It returns without writing and
    /// without an audit entry, instead of throwing and forcing every such caller to catch.
    /// </remarks>
    public static bool IsSoleOwner(IEnumerable<ITeamMember> members, string userKey)
    {
        if (string.IsNullOrEmpty(userKey)) return false;

        var owners = members?.Where(m => m != null && m.AccessLevel == AccessLevel.Owner).ToArray() ?? [];

        return owners.Length == 1 && owners[0].Key == userKey;
    }

    /// <summary>
    /// The sitting owners displaced by making <paramref name="newOwnerUserKey"/> owner — every member at
    /// <see cref="AccessLevel.Owner"/> except the incoming one.
    /// </summary>
    /// <remarks>
    /// Returns <b>all</b> of them, not just one. A team synced from a system that permits several owners is
    /// reduced to exactly one in a single operation; demoting one at a time would leave the invariant broken
    /// between calls and give the audit log a partial story.
    /// </remarks>
    public static IReadOnlyList<ITeamMember> OwnersToDemote(IEnumerable<ITeamMember> members, string newOwnerUserKey)
        => members?
               .Where(m => m != null && m.AccessLevel == AccessLevel.Owner && m.Key != newOwnerUserKey)
               .ToArray()
           ?? [];
}
