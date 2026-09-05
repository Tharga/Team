namespace Tharga.Team;

/// <summary>
/// How far this deployment has read the support mailbox.
/// </summary>
/// <param name="UidValidity">
/// The mailbox's UID generation. A server changing this makes every stored UID refer to a different
/// message, so the position must be discarded rather than trusted.
/// </param>
/// <param name="LastUid">The highest UID already considered. Zero means nothing has been read.</param>
public readonly record struct SupportMailPosition(uint UidValidity, uint LastUid)
{
    /// <summary>Nothing has been read yet.</summary>
    public static SupportMailPosition Start => new(0, 0);
}

/// <summary>
/// Remembers how far the support mailbox has been read, so a restart does not re-read it from the start.
/// </summary>
/// <remarks>
/// <b>A position rather than a flag, and that is the whole design.</b> The mailbox may be read by two
/// applications — two sites sharing one <c>support@</c> address is the case this was built for — so
/// "handled" cannot live in shared mailbox state. Setting <c>\Seen</c> or moving a message would hide it from
/// the instance that actually wanted it. Each deployment keeps its own position and ignores what is not
/// addressed to it.
/// <para>
/// <b>The key is what separates those deployments</b>, and getting it wrong loses mail silently: two
/// instances sharing a mailbox <i>and</i> a database must not share a position, or the first to poll
/// advances past a message addressed to the second and the second never sees it. It is derived from the
/// recipients the instance answers for, which is exactly what makes the two different.
/// </para>
/// <para>
/// <b>Losing a position is safe, and that is deliberate.</b> A missing or stale record means the mailbox is
/// re-read, and <see cref="ISupportEventLedger"/> then recognises everything already handled. So this is a
/// cost optimisation with a correctness story behind it, not a system of record — which is why the store is
/// optional and a host without one simply keeps its position in memory.
/// </para>
/// <para>
/// <b>It duplicates the transport's own position type</b> rather than sharing it. Everything under
/// <c>Tharga.Team.Support.Email</c> is about mail and only mail, so that it can be lifted out as a move
/// rather than a rewrite; a port declared here cannot reach into it. Two small records that must agree are
/// cheaper than a boundary that leaks, and the poller is the one place that converts between them.
/// </para>
/// </remarks>
public interface ISupportMailPositionStore
{
    /// <summary>
    /// Reads the position for <paramref name="key"/>, or <see cref="SupportMailPosition.Start"/> when there
    /// is none.
    /// </summary>
    /// <param name="key">Identifies the deployment, not the mailbox. See the remarks.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    Task<SupportMailPosition> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Records how far the mailbox has been read.</summary>
    /// <param name="key">Identifies the deployment, not the mailbox.</param>
    /// <param name="position">The new position.</param>
    /// <param name="cancellationToken">Abandons the write.</param>
    Task SetAsync(string key, SupportMailPosition position, CancellationToken cancellationToken = default);
}
