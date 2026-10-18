namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// Row-level facts about a team derived from its member list — who owns it, when it was last used, and
/// how its membership splits between accepted members and outstanding invitations.
/// </summary>
/// <remarks>
/// Pure and static so it is unit-testable: the project has no bUnit, so a decision left in razor markup
/// is unreachable from tests. Mirrors <see cref="Team.MemberHighlight"/> and
/// <see cref="Team.TeamVisibility"/>.
/// </remarks>
public static class TeamActivity
{
    /// <summary>
    /// When anyone last used this team, or null if no member ever has.
    /// </summary>
    /// <remarks>
    /// <see cref="ITeamMember.LastSeen"/> tracks <i>activity in the team</i>, so the maximum across members
    /// reads as "when this team was last used" — not "when its most active member last signed in", which
    /// is <see cref="IUser.LastSeen"/> and a different question: a member active only in another team
    /// doesn't count here.
    /// </remarks>
    public static DateTime? LastUsed(IEnumerable<TeamMemberInfo> members)
        => members?.Where(m => m.LastSeen.HasValue).Max(m => m.LastSeen);

    /// <summary>
    /// The team's owner, or null when no member holds <see cref="AccessLevel.Owner"/>. An ownerless team
    /// is a data defect worth seeing rather than hiding behind a blank cell.
    /// </summary>
    public static TeamMemberInfo Owner(IEnumerable<TeamMemberInfo> members)
        => members?.FirstOrDefault(m => m.AccessLevel == AccessLevel.Owner);

    /// <summary>
    /// Members who have accepted, separated from those still invited. A team listing "5" may be one
    /// member and four abandoned invitations — a distinction a single total cannot make, and the one a
    /// decision to delete the team usually turns on.
    /// </summary>
    /// <remarks>
    /// <see cref="MembershipState.Rejected"/> counts as neither: the invitation is closed and the person
    /// is not a member, so counting it either way would overstate one of the two figures.
    /// </remarks>
    public static (int Active, int Invited) CountByState(IEnumerable<TeamMemberInfo> members)
    {
        if (members == null) return (0, 0);

        var byState = members.ToArray();
        return (
            byState.Count(m => m.State is null or MembershipState.Member),
            byState.Count(m => m.State == MembershipState.Invited));
    }
}
