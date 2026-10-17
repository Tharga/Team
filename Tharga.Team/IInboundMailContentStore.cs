namespace Tharga.Team;

/// <summary>
/// Keeps the raw content of received mail where the host wants it — blob storage, a file share — so nothing
/// large reaches the toolkit's own store.
/// </summary>
/// <remarks>
/// <b>Keyed by the mail's id, and written before the mail is recorded.</b> The same message read twice — by two
/// instances, or again after a crash — gets the same id, so saving twice writes the same thing twice and is
/// harmless. <see cref="SaveAsync"/> must therefore overwrite, never fail on an id it already holds.
/// <para>
/// Called only by the toolkit. A caller reads content through the mailbox or postmaster service, where access
/// is checked; this port checks nothing.
/// </para>
/// </remarks>
public interface IInboundMailContentStore
{
    /// <summary>Keeps the raw message under the mail's id, replacing whatever was kept under it before.</summary>
    Task SaveAsync(string mailId, byte[] raw, CancellationToken cancellationToken = default);

    /// <summary>The raw message, or <c>null</c> when none is kept under this id.</summary>
    Task<byte[]> GetAsync(string mailId, CancellationToken cancellationToken = default);

    /// <summary>Removes the raw message; nothing happens when none is kept.</summary>
    Task DeleteAsync(string mailId, CancellationToken cancellationToken = default);
}
