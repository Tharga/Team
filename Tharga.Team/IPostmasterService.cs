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
/// </remarks>
public interface IPostmasterService
{
    /// <summary>A page of unrouted mail, newest first.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterRead)]
    Task<PostmasterPage> ListAsync(string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>How much unrouted mail is held.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterRead)]
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>One unrouted mail, or null when the postmaster holds none by this id.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterRead, Audit = AuditMode.Access)]
    Task<InboundMailItem> OpenAsync(string mailId, CancellationToken cancellationToken = default);

    /// <summary>An unrouted mail as it arrived, or null when the postmaster holds none by this id.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterRead, Audit = AuditMode.Access)]
    Task<MailContent> GetContentAsync(string mailId, CancellationToken cancellationToken = default);

    /// <summary>Delivers an unrouted mail to a team, marked as rerouted.</summary>
    [RequireScope(SystemMailboxScopes.PostmasterManage, Audit = AuditMode.Change)]
    Task<InboundMailItem> RouteAsync(string mailId, string teamKey, CancellationToken cancellationToken = default);
}
