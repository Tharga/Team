namespace Tharga.Team;

/// <summary>Where a request for access to a team stands.</summary>
public enum TeamAccessRequestStatus
{
    /// <summary>Waiting for a team manager to decide.</summary>
    Pending,

    /// <summary>Approved — the team's consent was set for the requested window.</summary>
    Approved,

    /// <summary>Denied by a team manager.</summary>
    Denied,

    /// <summary>Withdrawn by the requester, or replaced by a newer request from them.</summary>
    Cancelled
}

/// <summary>
/// A request, by someone holding a consent role, for access to a team they are not a member of.
/// </summary>
/// <remarks>
/// <b>Approving grants through the team's consent</b>, not to the requester personally: every configured consent
/// role reaches the team at <see cref="AccessLevel"/> until <see cref="GrantedUntil"/>. That is how consent works in
/// this toolkit, and the approval screen says so.
/// <para>
/// <b>A duration is requested, not an end time.</b> The window starts when the request is approved, so time spent
/// waiting for a decision does not use it up.
/// </para>
/// </remarks>
public sealed record TeamAccessRequest
{
    /// <summary>Identifies the request within its team.</summary>
    public required string Id { get; init; }

    /// <summary>The requester's user key.</summary>
    public required string RequesterKey { get; init; }

    /// <summary>The requester's display name when the request was made, for the approver.</summary>
    public string RequesterName { get; init; }

    /// <summary>The level asked for. One of <see cref="TeamAccessRequestRules.RequestableLevels"/>.</summary>
    public required AccessLevel AccessLevel { get; init; }

    /// <summary>How long access should last once approved. Null asks for no end.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Why access is needed, as the requester wrote it. Optional.</summary>
    public string Message { get; init; }

    /// <summary>When the request was made (UTC).</summary>
    public required DateTime RequestedAt { get; init; }

    /// <summary>Where the request stands.</summary>
    public TeamAccessRequestStatus Status { get; init; } = TeamAccessRequestStatus.Pending;

    /// <summary>The user key of whoever approved, denied or cancelled it.</summary>
    public string DecidedBy { get; init; }

    /// <summary>When it was approved, denied or cancelled (UTC).</summary>
    public DateTime? DecidedAt { get; init; }

    /// <summary>For an approved request with a duration, when the consent it granted runs out (UTC).</summary>
    public DateTime? GrantedUntil { get; init; }
}

/// <summary>
/// A consent that runs out, and what the team's consent returns to when it does.
/// </summary>
/// <remarks>
/// Recorded when a time-bound access request is approved. The consent in force is always read through
/// <see cref="TeamConsent.Resolve"/>, which applies this — nothing needs to run at the moment of expiry.
/// </remarks>
public sealed record TemporaryConsent
{
    /// <summary>When the temporary consent stops applying (UTC).</summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>The consented roles before the temporary consent. Null or empty for none.</summary>
    public string[] PreviousConsentedRoles { get; init; }

    /// <summary>The consent level before the temporary consent.</summary>
    public AccessLevel? PreviousConsentAccessLevel { get; init; }
}

/// <summary>The fixed rules for team access requests.</summary>
public static class TeamAccessRequestRules
{
    /// <summary>
    /// The levels that can be requested. Owner is not requestable, and Custom grants no base scopes, so neither is
    /// offered.
    /// </summary>
    public static IReadOnlyList<AccessLevel> RequestableLevels { get; } = [AccessLevel.Viewer, AccessLevel.User, AccessLevel.Administrator];

    /// <summary>How many decided requests a team keeps, newest first. Pending requests are always kept.</summary>
    public const int DecidedHistoryLimit = 20;

    /// <summary>The longest message a requester may attach.</summary>
    public const int MaxMessageLength = 1000;
}
