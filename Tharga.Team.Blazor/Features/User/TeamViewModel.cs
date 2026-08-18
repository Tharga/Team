using Tharga.Team.Blazor.Features.Team;

namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// View model for a team row in the teams list view.
/// </summary>
public record TeamViewModel
{
    public string Key { get; init; }
    public string Name { get; init; }

    /// <summary>
    /// Every member row, accepted or not. Kept as the total for compatibility — use
    /// <see cref="ActiveMemberCount"/> and <see cref="InvitedCount"/> to tell the two apart.
    /// </summary>
    public int MemberCount { get; init; }

    public TeamMemberInfo[] Members { get; init; }

    /// <summary>Reference to the team's uploaded icon, or null. Resolved through <c>TeamAvatar</c>.</summary>
    public string Icon { get; init; }

    /// <summary>
    /// Display name of the member holding <see cref="AccessLevel.Owner"/>, or null when the team has no
    /// owner — a data defect worth surfacing rather than hiding.
    /// </summary>
    public string OwnerName { get; init; }

    /// <summary>
    /// Whether the team has no member at <see cref="AccessLevel.Owner"/> — one of the two states
    /// <see cref="SystemTeamScopes.SetOwner"/> repairs.
    /// </summary>
    /// <remarks>
    /// Deliberately not derived from <see cref="OwnerName"/> being null. That is also null for an owner
    /// with no display name, and offering to "repair" a team that has an owner would produce an action
    /// the service refuses.
    /// </remarks>
    public bool IsOwnerless { get; init; }

    /// <summary>
    /// How many members hold <see cref="AccessLevel.Owner"/>. Normally one.
    /// </summary>
    /// <remarks>
    /// <b>More than one is a real state, not a bug in this view.</b> A team synced from a system whose model
    /// permits several owners arrives carrying them, and reducing it to one is a case
    /// <see cref="SystemTeamScopes.SetOwner"/> exists for. The count decides only the <i>wording</i> of the
    /// action — authorization is the scope alone.
    /// </remarks>
    public int OwnerCount { get; init; }

    /// <summary>
    /// When anyone last used this team, or null if nobody ever has. Derived from the members'
    /// <see cref="ITeamMember.LastSeen"/>, which tracks team selection.
    /// </summary>
    public DateTime? LastUsed { get; init; }

    /// <summary>Members who have accepted their membership.</summary>
    public int ActiveMemberCount { get; init; }

    /// <summary>Invitations still outstanding — neither accepted nor rejected.</summary>
    public int InvitedCount { get; init; }

    /// <summary>
    /// What this team has consented to grant an oversight caller — the access level, or null when it has
    /// consented to nothing. Only meaningful to a caller holding the <c>teams:read</c> system scope;
    /// otherwise every listed team is one the caller belongs to.
    /// </summary>
    public AccessLevel? Consent { get; init; }
}
