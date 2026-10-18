namespace Tharga.Team;

/// <summary>
/// An unrouted mail as the postmaster lists it: who sent it, about what and when, and nothing more until it is opened.
/// </summary>
/// <remarks>
/// <b>Deliberately thin.</b> Unrouted mail may hold any team's material, so listing it reveals only what lets
/// someone decide what to open, and opening is the audited act.
/// </remarks>
public record PostmasterMailSummary
{
    public required string Id { get; init; }

    public required string From { get; init; }

    public string Subject { get; init; }

    public required DateTime ReceivedAt { get; init; }

    public required MailRouteReason Reason { get; init; }

    /// <summary>When the mail was archived; null while it is held.</summary>
    public DateTime? ArchivedAt { get; init; }

    /// <summary>The name of whoever archived it, as it was then; null while it is held.</summary>
    public string ArchivedBy { get; init; }
}
