namespace Tharga.Team;

/// <summary>
/// The persistence port for support cases — what the domain needs from a store, in the domain's language.
/// </summary>
/// <remarks>
/// <b>A port, not a repository.</b> Nothing here names or inherits a storage type: no <c>IRepository</c>
/// base, no driver types, no filter objects. <c>IApiKeyRepository : IRepository</c> is the shape this
/// deliberately avoids — a port defined in one store's terms means the second adapter has to implement the
/// first store's idea of persistence.
/// <para>
/// <b>Every method takes the team.</b> A case id alone never identifies a case, so the tenant boundary is
/// expressed by the port itself rather than trusted to each caller to remember. That is what makes the
/// cross-tenant read hard to write by accident.
/// </para>
/// <para>
/// <b>Two methods carry a message alongside a state change, and that is the atomicity contract.</b> Raising
/// a case creates a case <i>and</i> its first message; closing one sets a status <i>and</i> records why. An
/// adapter must apply each as a single unit, or a crash leaves a case with no transcript and the model's
/// central promise — a case always has at least one message — becomes untrue.
/// </para>
/// </remarks>
public interface ISupportCaseStore
{
    /// <summary>One case, or <c>null</c> if the team has no such case.</summary>
    Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>Every case in a team, newest first.</summary>
    Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Cases raised by one author, newest first.</summary>
    Task<SupportCasePage> GetCasesByAuthorAsync(string teamKey, string authorIdentity, string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>A case's transcript in order, oldest first.</summary>
    Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Creates a case together with its first message, as one unit.</summary>
    Task AddCaseAsync(SupportCase supportCase, SupportMessage firstMessage, CancellationToken cancellationToken = default);

    /// <summary>Appends one entry to a case's transcript.</summary>
    Task AppendMessageAsync(string teamKey, string caseId, SupportMessage message, CancellationToken cancellationToken = default);

    /// <summary>Closes a case and records the closure in its transcript, as one unit.</summary>
    Task CloseCaseAsync(string teamKey, string caseId, DateTime closedAt, string closedBy, SupportMessage closureMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a closed case again and records it in the transcript, as one unit.
    /// </summary>
    /// <remarks>
    /// <b>Clears the closure entirely</b> — status, timestamp and actor. A case left carrying who closed it
    /// while open would read as closed to anything deriving from that, including
    /// <see cref="SupportCase.ClosedReason"/>.
    /// <para>
    /// <b>Reopening keeps the history.</b> That is the whole point of it existing rather than telling somebody
    /// to raise a second case: the conversation that explains the problem is the one already written down.
    /// </para>
    /// <para>
    /// <b>Throws rather than defaulting to nothing</b>, because a silent no-op would leave a case closed while
    /// the caller was told it had reopened. A store written before this existed keeps compiling; it says
    /// plainly that it cannot do this if asked.
    /// </para>
    /// </remarks>
    Task ReopenCaseAsync(string teamKey, string caseId, SupportMessage reopenMessage, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(ReopenCaseAsync)}. Implement it to let a closed " +
            "support case be opened again.");

    /// <summary>
    /// Open cases whose newest entry was written by support before
    /// <paramref name="lastActivityBefore"/> — the ones the inactivity sweep may close.
    /// </summary>
    /// <remarks>
    /// <b>The store owns the whole predicate, not just the cheap half.</b> "Support wrote the newest entry"
    /// needs the last element of an embedded transcript, which is not an indexable filter — but the transcript
    /// arrives with the document, so a store can narrow on indexed fields and then check the tail without a
    /// second read. Splitting it, so a caller re-reads each candidate to inspect it, would turn one query into
    /// one query per case.
    /// <para>
    /// <b>A system entry is not support answering.</b> A case whose newest entry is the toolkit's own — a
    /// reopen note — must not be returned, or reopening a case would arm the very clock that closes it.
    /// </para>
    /// <para>
    /// <b>Not team-scoped, because a sweep has no caller and no team.</b> It runs as framework code on
    /// nobody's behalf; see also <see cref="GetCaseByBindingAsync"/>.
    /// </para>
    /// <para>
    /// <b>Defaults to nothing</b>, so a store written before this existed keeps compiling and simply never
    /// auto-closes.
    /// </para>
    /// </remarks>
    /// <param name="lastActivityBefore">Cases untouched since this moment are eligible.</param>
    /// <param name="limit">Most cases to return, so one sweep cannot load an unbounded set.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    Task<SupportCase[]> GetCasesForInactivityCloseAsync(DateTime lastActivityBefore, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult<SupportCase[]>([]);

    /// <summary>
    /// Cases belonging to no team, newest first, or an empty page when the store cannot answer.
    /// </summary>
    /// <remarks>
    /// <b>Not team-scoped, because there is no team</b> — the same shape as
    /// <see cref="GetCaseByBindingAsync"/>, and reached only from a caller already checked against
    /// <see cref="SystemSupportScopes.Read"/>.
    /// <para><b>Defaults to nothing</b>, so a store written before unassigned cases existed keeps compiling.</para>
    /// </remarks>
    Task<SupportCasePage> GetUnassignedCasesAsync(string cursor, int pageSize, CancellationToken cancellationToken = default)
        => Task.FromResult(new SupportCasePage { Items = [] });

    /// <summary>
    /// Gives an unassigned case to a team, returning whether this call did it.
    /// </summary>
    /// <remarks>
    /// <b>Conditional on the case still having no team</b>, so two agents triaging the same queue cannot
    /// both assign it and the second is told it changed nothing. The same shape as
    /// <see cref="TryCloseForInactivityAsync"/>: let the write decide, rather than reading and then acting
    /// on what was true a moment ago.
    /// </remarks>
    Task<bool> TryAssignCaseAsync(string caseId, string teamKey, SupportMessage assignmentMessage, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    /// <summary>
    /// Closes a case for inactivity, but only if it is still open. Returns whether this call closed it.
    /// </summary>
    /// <remarks>
    /// <b>The condition is the point.</b> Two instances sweeping together both see the same case; the update
    /// applies only while the status is still open, so exactly one closes it and the other is told it did
    /// not. That is the same shape <see cref="ISupportEventLedger"/> uses — let the write decide, rather than
    /// reading and then acting on what was true a moment ago.
    /// <para>
    /// <b>The actor is the store's to set</b>, not the caller's: it records
    /// <see cref="SupportCaseActors.AutoClose"/>, which is what <see cref="SupportCase.ClosedReason"/> reads.
    /// A caller that could pass its own actor could make an automatic closure claim to be a person's.
    /// </para>
    /// <para><b>Defaults to closing nothing</b>, for the same reason as the query above.</para>
    /// </remarks>
    Task<bool> TryCloseForInactivityAsync(string teamKey, string caseId, DateTime closedAt, SupportMessage closureMessage, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    /// <summary>
    /// The case projected onto a given channel identifier, or <c>null</c> if no case is bound to it.
    /// </summary>
    /// <remarks>
    /// <b>The only read here that is not scoped by team</b>, and it has to be: an inbound event arrives
    /// carrying a channel's identifier and nothing else, so the binding is what resolves the team rather
    /// than something the caller supplies. It is reached only from a channel adapter handling a verified
    /// event, never from a user-facing path.
    /// </remarks>
    Task<SupportCase> GetCaseByBindingAsync(SupportChannelType channelType, string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The case with this id whatever team owns it, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <b>For inbound mail that names a case but not a team</b>, which is what a per-case reply address
    /// (<c>support+{caseId}@…</c>) carries when the sender's client dropped the threading headers. Like
    /// <see cref="GetCaseByBindingAsync"/> it is not scoped by team and for the same reason: the identifier
    /// is what resolves the team, not the caller. Reached only from a channel adapter, never from a
    /// user-facing path.
    /// <para>
    /// <b>Defaults to finding nothing</b>, so a store written before this existed keeps compiling and keeps
    /// working — the threading headers are the primary match and this is only the fallback. A store that
    /// cannot look up by id alone is answering honestly rather than failing.
    /// </para>
    /// </remarks>
    Task<SupportCase> GetCaseByIdAsync(string caseId, CancellationToken cancellationToken = default)
        => Task.FromResult<SupportCase>(null);

    /// <summary>
    /// Records that someone has read a case up to <paramref name="sequence"/>.
    /// </summary>
    /// <remarks>
    /// Idempotent, and must not grow the document: one entry per person, updated in place. Somebody opening
    /// a case fifty times leaves one entry.
    /// </remarks>
    Task MarkReadAsync(string teamKey, string caseId, string identity, int sequence, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of this person's own cases hold entries they have not read.
    /// </summary>
    Task<int> GetUnreadCountAsync(string teamKey, string identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many cases in the team are waiting on support — their newest entry came from the person who
    /// raised them.
    /// </summary>
    Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Records a case's projection onto an external channel.</summary>
    Task AddBindingAsync(string teamKey, string caseId, SupportChannelBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records whether one transcript entry reached the case's channel.
    /// </summary>
    /// <remarks>
    /// A second write after the message itself, deliberately. The case is written first and is authoritative;
    /// the channel is a projection, so a channel that is slow or down must not delay or block the record of
    /// what somebody said.
    /// </remarks>
    Task SetMessageDeliveryAsync(string teamKey, string caseId, int sequence, SupportMessageDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Destroys every case belonging to a team. Backs the purge cascade.
    /// </summary>
    /// <remarks>
    /// Purging a team drops the host's per-team database, which does not reach the toolkit's own shared
    /// collections — so without this the cases would outlive the team that owned them.
    /// </remarks>
    Task<int> DeleteCasesForTeamAsync(string teamKey, CancellationToken cancellationToken = default);
}
