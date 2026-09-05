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

    /// <summary>
    /// The owning team, or <c>null</c> when the case is not assigned to one.
    /// </summary>
    /// <remarks>
    /// <b>Unassigned is a durable state, not a staging area.</b> A case that arrived by mail from somebody
    /// who belongs to several teams — or to none the toolkit can see — belongs to no team until a support
    /// agent says which, and it can be answered, closed and reopened meanwhile. The person who knows which
    /// tenant a problem concerns is a human reading it, not an algorithm reading a <c>From:</c> header.
    /// <para>
    /// <b>The invariant it replaces still holds where there is a team.</b> A case with one is loaded through
    /// it and authorized by membership exactly as before, so holding a case id from another tenant still
    /// gains nothing. What changed is that "no team" became expressible, not that team scoping became
    /// optional.
    /// </para>
    /// <para>
    /// <b>An unassigned case is governed system-wide</b>, because a team scope has no team to be held
    /// against. See <see cref="Support.Cases.ISupportCaseService.AssignCaseAsync"/>.
    /// </para>
    /// </remarks>
    public string TeamKey { get; init; }

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
    /// <remarks>
    /// May be <see cref="SupportCaseActors.AutoClose"/>, when the case closed itself rather than being closed
    /// by a person.
    /// </remarks>
    public string ClosedBy { get; init; }

    /// <summary>
    /// Why the case is closed, or <c>null</c> while it is open.
    /// </summary>
    /// <remarks>
    /// <b>Derived from <see cref="ClosedBy"/> rather than stored</b>, and deliberately so. The store already
    /// records who closed a case; adding a reason to <see cref="ISupportCaseStore.CloseCaseAsync"/> would
    /// change a signature that hosts implement for their own storage, which is a compile-time break in
    /// somebody else's repository for a value that can be read from what is already there.
    /// <para>
    /// It follows that the reason is exactly as trustworthy as the actor, which is the right coupling: a case
    /// closed by the sweeper is closed for inactivity by definition, not by a flag that could disagree with
    /// who closed it.
    /// </para>
    /// </remarks>
    public SupportCaseClosureReason? ClosedReason
        => Status != SupportCaseStatus.Closed
            ? null
            : ClosedBy == SupportCaseActors.AutoClose
                ? SupportCaseClosureReason.Inactivity
                : SupportCaseClosureReason.Manual;

    /// <summary>Number of entries in the transcript, so a list can be rendered without reading it.</summary>
    public required int MessageCount { get; init; }

    /// <summary>External projections. Empty until the channel work lands.</summary>
    public SupportChannelBinding[] Bindings { get; init; } = [];
}
