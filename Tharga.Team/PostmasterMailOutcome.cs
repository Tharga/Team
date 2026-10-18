namespace Tharga.Team;

/// <summary>
/// The <see cref="InboundMailItem.Outcome"/> the postmaster records on mail it holds.
/// </summary>
/// <remarks>
/// A mail the postmaster holds has no outcome until it is archived, and routing it to a team clears the outcome, so
/// a team never receives a mail already marked as handled. A team's own outcomes are the host's, in its own terms.
/// </remarks>
public static class PostmasterMailOutcome
{
    /// <summary>Put away without routing — spam, sent by mistake, dealt with elsewhere; <see cref="InboundMailItem.HandledAt"/> and <see cref="InboundMailItem.HandledBy"/> say when and by whom.</summary>
    public const string Archived = "archived";
}
