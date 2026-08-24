namespace Tharga.Team;

/// <summary>
/// A support conversation belonging to a team.
/// </summary>
/// <remarks>
/// <b>The team owns the case; the author is a link that may dangle.</b> A case is raised by a person but is
/// about the team's account, and the team is what still exists a year later. Deleting a user runs
/// <c>ITeamService.RemoveUserFromAllTeamsAsync</c>, which strips that person's membership from every team —
/// so a case keyed on a member would be orphaned by design. Authorization keys on <see cref="TeamKey"/>
/// alone, and no check requires the author to still resolve.
/// <para>
/// <b>The transcript is not carried here.</b> A case can accumulate years of messages, so history is read
/// separately and paged; see <see cref="ISupportCaseStore.GetMessagesAsync"/>. This record is the header.
/// </para>
/// </remarks>
public record SupportCase
{
    public required string Id { get; init; }

    /// <summary>The owning team. Every read is scoped by it; an id alone never identifies a case.</summary>
    public required string TeamKey { get; init; }

    /// <summary>
    /// The stable authentication subject of whoever raised the case. May no longer resolve to a user or a
    /// member, which is expected rather than exceptional.
    /// </summary>
    public required string AuthorIdentity { get; init; }

    /// <summary>The author's display name as it was when the case was raised.</summary>
    public required string AuthorName { get; init; }

    public required string Subject { get; init; }

    public required SupportCaseStatus Status { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? ClosedAt { get; init; }

    /// <summary>The stable subject of whoever closed the case, or <c>null</c> while it is open.</summary>
    public string ClosedBy { get; init; }

    /// <summary>Number of entries in the transcript, so a list can be rendered without reading it.</summary>
    public required int MessageCount { get; init; }

    /// <summary>External projections. Empty until the channel work lands.</summary>
    public SupportChannelBinding[] Bindings { get; init; } = [];
}
