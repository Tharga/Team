namespace Tharga.Team;

/// <summary>
/// Keeps received mail, for a team's mailbox and for the postmaster. The persistence port behind
/// <see cref="ITeamMailboxService"/> and <see cref="IPostmasterService"/>.
/// </summary>
/// <remarks>
/// <b>A mail with no team is the postmaster's.</b> Every team-scoped read names its team, and a mail held by the
/// postmaster is reachable only through the postmaster operations, so a team id never reaches unrouted mail.
/// <para>
/// <b>Adding is the deduplication.</b> A mail's id is derived from what it is — the same delivery read twice
/// gets the same id — and <see cref="AddAsync"/> refuses an id it already holds, durably. Two instances reading
/// the same mailbox, or one re-reading after a crash, therefore store each delivery once.
/// </para>
/// </remarks>
public interface ITeamMailStore
{
    /// <summary>Keeps a received mail; false, and nothing written, when a mail with this id is already kept.</summary>
    Task<bool> AddAsync(InboundMailItem item, CancellationToken cancellationToken = default);

    /// <summary>One of a team's mails, or null when the team has none by this id.</summary>
    Task<InboundMailItem> GetAsync(string teamKey, string mailId, CancellationToken cancellationToken = default);

    /// <summary>A page of a team's mail, newest first.</summary>
    Task<InboundMailPage> ListAsync(string teamKey, InboundMailFilter filter, string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>How many of a team's mails nobody has handled.</summary>
    Task<int> CountUnhandledAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Records what became of one of a team's mails; null when the team has none by this id.</summary>
    Task<InboundMailItem> SetOutcomeAsync(string teamKey, string mailId, string outcome, MailThreadOwner owner, string handledBy, DateTime handledAt, CancellationToken cancellationToken = default);

    /// <summary>One mail the postmaster holds, or null when it holds none by this id.</summary>
    Task<InboundMailItem> GetUnroutedAsync(string mailId, CancellationToken cancellationToken = default);

    /// <summary>A page of the mail the postmaster holds, newest first.</summary>
    Task<InboundMailPage> ListUnroutedAsync(string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>How much mail the postmaster holds.</summary>
    Task<int> CountUnroutedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a mail from the postmaster to a team, marked as rerouted; null, and nothing changed, when the
    /// postmaster no longer holds it — someone else routed it first.
    /// </summary>
    Task<InboundMailItem> RouteAsync(string mailId, string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Removes the postmaster's mail received before a moment, and says which, so their content can go too.</summary>
    Task<string[]> DeleteUnroutedReceivedBeforeAsync(DateTime receivedBefore, CancellationToken cancellationToken = default);

    /// <summary>Removes every mail a team holds, when the team itself is removed, and says which.</summary>
    Task<string[]> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default);
}
