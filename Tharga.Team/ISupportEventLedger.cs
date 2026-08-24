namespace Tharga.Team;

/// <summary>
/// Remembers which inbound channel events have already been handled, so a redelivery is not applied twice.
/// </summary>
/// <remarks>
/// <b>Slack retries are guaranteed, not exceptional.</b> Any non-200, any timeout, any response slower than
/// three seconds, and the same event arrives again carrying the same id. Without a ledger a retry appends the
/// same reply to the case a second time, which is the failure a user actually notices.
/// <para>
/// <b>It must be shared across instances</b>, for the same reason <see cref="ITeamCache"/> exists: with a
/// process-local set, two instances behind a load balancer each accept the same retry and the deduplication
/// achieves nothing. <see cref="ITeamCache"/> itself is deliberately not reused — it is purpose-built for
/// three named claims lookups and has no general key/value surface, so putting an event id through it would
/// be an abuse of a security-sensitive cache rather than a fit.
/// </para>
/// <para>
/// <b>Record-and-report, not check-then-act.</b> The single method exists so an implementation can make the
/// decision atomic — a unique index, an insert that fails on conflict. A separate "have I seen this?"
/// followed by "remember it" is a race that two instances lose at exactly the moment retries arrive
/// together, which is precisely when it matters.
/// </para>
/// </remarks>
public interface ISupportEventLedger
{
    /// <summary>
    /// Records an event as handled, returning <c>true</c> when it was new and <c>false</c> when it had
    /// already been recorded.
    /// </summary>
    /// <param name="source">Which channel the event came from, so two channels cannot collide on an id.</param>
    /// <param name="eventId">The channel's own identifier for the delivery.</param>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    Task<bool> TryRecordAsync(string source, string eventId, CancellationToken cancellationToken = default);
}
