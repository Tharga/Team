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

    /// <summary>One mail the postmaster holds, archived or not, or null when it holds none by this id.</summary>
    Task<InboundMailItem> GetUnroutedAsync(string mailId, CancellationToken cancellationToken = default);

    /// <summary>A page of the mail the postmaster holds and nobody archived, newest first.</summary>
    Task<InboundMailPage> ListUnroutedAsync(string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the postmaster's mail in one state, newest received first: held is mail with no outcome, archived
    /// is mail whose outcome is <see cref="PostmasterMailOutcome.Archived"/>.
    /// </summary>
    /// <remarks>A default member so a host's own store keeps compiling; it lists held mail through the call above and refuses archived mail until implemented.</remarks>
    Task<InboundMailPage> ListUnroutedAsync(PostmasterMailState state, string cursor, int pageSize, CancellationToken cancellationToken = default)
        => state == PostmasterMailState.Held
            ? ListUnroutedAsync(cursor, pageSize, cancellationToken)
            : throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(ListUnroutedAsync)} by state. Implement it to list the mail the postmaster archived.");

    /// <summary>How much mail the postmaster holds that nobody archived.</summary>
    Task<int> CountUnroutedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a mail from the postmaster to a team, marked as rerouted and with any outcome cleared, so it arrives
    /// unhandled; null, and nothing changed, when the postmaster no longer holds it — someone else routed it first.
    /// </summary>
    Task<InboundMailItem> RouteAsync(string mailId, string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a held mail <see cref="PostmasterMailOutcome.Archived"/>, with who and when, in one conditional write;
    /// null, and nothing changed, when the postmaster holds no mail by this id or it is already archived.
    /// </summary>
    Task<InboundMailItem> ArchiveUnroutedAsync(string mailId, string archivedBy, DateTime archivedAt, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(ArchiveUnroutedAsync)}. Implement it to let the postmaster archive mail.");

    /// <summary>
    /// Clears the outcome, and who and when, of an archived mail in one conditional write; null, and nothing changed,
    /// when the postmaster holds no archived mail by this id.
    /// </summary>
    Task<InboundMailItem> RestoreUnroutedAsync(string mailId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(RestoreUnroutedAsync)}. Implement it to let the postmaster restore archived mail.");

    /// <summary>Removes the postmaster's mail received before a moment, archived or not, and says which, so their content can go too.</summary>
    Task<string[]> DeleteUnroutedReceivedBeforeAsync(DateTime receivedBefore, CancellationToken cancellationToken = default);

    /// <summary>Removes every mail a team holds, when the team itself is removed, and says which.</summary>
    Task<string[]> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default);
}
