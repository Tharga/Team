namespace Tharga.Team;

public interface ITeamMember
{
    public string Key { get; }
    public string Name { get; }
    public Invitation Invitation { get; }
    public DateTime? LastSeen { get; }
    public MembershipState? State { get; }
    /// <summary>
    /// The member's level in the team, or null when none was ever set. <b>Null grants nothing</b>: no scopes, no
    /// <c>AccessLevel</c> claim, and it ranks below every level. Before 4.0 an unset level read as <see cref="Tharga.Team.AccessLevel.Owner"/>,
    /// the enum's zero value.
    /// </summary>
    public AccessLevel? AccessLevel { get; }
    public string[] TenantRoles { get; }
    public string[] ScopeOverrides { get; }

    /// <summary>
    /// When this member's access to the team was suspended, or null if it is active. A suspended member
    /// keeps their membership, access level, roles and history, and still sees the team in the selector —
    /// they are granted no team scopes, so every scoped operation refuses.
    /// </summary>
    /// <remarks>
    /// Opt-in by shape, like <see cref="IUser.DisabledAt"/>: declare the property on the member entity to
    /// persist it, and override <c>TeamServiceBase.SetTeamMemberSuspendedAsync</c>.
    /// <para>
    /// <b>Deliberately not a fourth <see cref="MembershipState"/>.</b> Host stores list a user's teams by
    /// filtering <c>State == MembershipState.Member</c>, so a suspended state would remove the team from
    /// the selector entirely — the opposite of the intent, and unfixable from inside the toolkit because
    /// that filter lives in host code. Keeping <see cref="State"/> at <see cref="MembershipState.Member"/>
    /// means every existing query keeps working untouched.
    /// </para>
    /// </remarks>
    public DateTime? SuspendedAt => null;

    /// <summary>Who suspended this member, or null.</summary>
    public string SuspendedBy => null;
}