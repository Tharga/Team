namespace Tharga.Team;

/// <summary>
/// The postmaster: mail that reached the shared mailbox at an address no team owns.
/// </summary>
/// <remarks>
/// <b>Wholly system-wide.</b> Unrouted mail belongs to no team, so only a system scope can govern it, and
/// nothing a team holds reaches it.
/// <para>
/// <b>Opening is the audited act.</b> A listing shows sender, subject and time only; reading a mail that may
/// hold another team's material is recorded every time, whatever the host's default audit mode.
/// </para>
/// <para>
/// <b>Held or archived.</b> A mail waits to be routed to a team or archived. Archiving puts it away without
/// delivering it anywhere — spam, a mail sent by mistake, one dealt with elsewhere — and restoring brings it back.
/// Either way it stays the postmaster's until the retention period deletes it.
/// </para>
/// </remarks>
public interface IPostmasterService
{
    /// <summary>A page of the mail the postmaster holds, archived mail left out, newest first.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterRead)]
    Task<PostmasterPage> ListAsync(string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>A page of the postmaster's mail in one state, the most recently received first.</summary>
    /// <remarks>
    /// Archived mail is listed in the order it arrived, as held mail is, not in the order it was archived; each
    /// summary says when it was archived and by whom.
    /// </remarks>
    [RequireScope(SystemMailboxScopes.PostmasterRead)]
    Task<PostmasterPage> ListAsync(PostmasterMailState state, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>How much unrouted mail is held, archived mail left out.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterRead)]
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>One unrouted mail, held or archived, or null when the postmaster holds none by this id.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterRead, Audit = AuditMode.Access)]
    Task<InboundMailItem> OpenAsync(string mailId, CancellationToken cancellationToken = default);

    /// <summary>An unrouted mail as it arrived, or null when the postmaster holds none by this id.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterRead, Audit = AuditMode.Access)]
    Task<MailContent> GetContentAsync(string mailId, CancellationToken cancellationToken = default);

    /// <summary>Delivers an unrouted mail to a team, marked as rerouted.</summary>
    /// <remarks>
    /// The team is where the mail goes, not who is asking: routing is authorized by the system scope alone, and
    /// the receiving team's grants play no part — which is why the parameter is not the <c>teamKey</c> a team
    /// service takes. An archived mail can be routed too, and arrives unhandled like any other.
    /// </remarks>
    [RequireScope(SystemMailboxScopes.PostmasterManage, Audit = AuditMode.Change)]
    Task<InboundMailItem> RouteAsync(string mailId, string toTeamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a held mail away without delivering it to any team, recording when and by whom; null when the
    /// postmaster holds no mail by this id, which includes one already routed.
    /// </summary>
    /// <remarks>
    /// The mail leaves the held list and the count, is listed as <see cref="PostmasterMailState.Archived"/>, and can
    /// still be opened, restored and routed. Archiving an archived mail returns it unchanged.
    /// </remarks>
    [RequireScope(SystemMailboxScopes.PostmasterManage, Audit = AuditMode.Change)]
    Task<InboundMailItem> ArchiveAsync(string mailId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings an archived mail back among the held mail; null when the postmaster holds no mail by this id. Restoring
    /// a held mail returns it unchanged.
    /// </summary>
    [RequireScope(SystemMailboxScopes.PostmasterManage, Audit = AuditMode.Change)]
    Task<InboundMailItem> RestoreAsync(string mailId, CancellationToken cancellationToken = default);
}
