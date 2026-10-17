namespace Tharga.Team;

/// <summary>
/// A team's mailbox: its address, and the mail that arrived at it.
/// </summary>
/// <remarks>
/// <b>Wholly team-bound.</b> Every operation names its team first, and a mail id alone never reaches a mail —
/// the store requires the team too — so holding an id from another team reaches nothing.
/// <para>
/// <b>Operations, not CRUD.</b> Issuing a key, rotating it and recording what became of a mail are separate
/// facts under separate scopes. Nothing here registers a mail anywhere: what a mail becomes is the host's,
/// and <see cref="SetOutcomeAsync"/> only records the host's answer.
/// </para>
/// </remarks>
public interface ITeamMailboxService
{
    /// <summary>The team's keys and addresses; <see cref="TeamMailAddresses.Primary"/> is null before a key is issued.</summary>
    [RequireScope(MailboxScopes.Read)]
    Task<TeamMailAddresses> GetAddressesAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The address of an item inside the team — <c>{key}{suffix}+{itemKey}@{domain}</c> on the primary key — or
    /// null when the team has no key yet.
    /// </summary>
    [RequireScope(MailboxScopes.Read)]
    Task<string> GetItemAddressAsync(string teamKey, string itemKey, CancellationToken cancellationToken = default);

    /// <summary>Issues the team's first key; does nothing when it already has one.</summary>
    [RequireScope(MailboxScopes.Manage, Audit = AuditMode.Change)]
    Task<TeamMailAddresses> EnsureKeyAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a new primary key and keeps the current one as the secondary until it retires; a previous
    /// secondary retires at once.
    /// </summary>
    [RequireScope(MailboxScopes.Manage, Audit = AuditMode.Change)]
    Task<TeamMailAddresses> RotateKeyAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>A page of the team's mail, newest first.</summary>
    [RequireScope(MailboxScopes.Read)]
    Task<InboundMailPage> ListMailAsync(string teamKey, InboundMailFilter filter = InboundMailFilter.Unhandled, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>How many of the team's mails nobody has handled.</summary>
    [RequireScope(MailboxScopes.Read)]
    Task<int> GetUnhandledCountAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>One mail, or null when the team has none by this id.</summary>
    [RequireScope(MailboxScopes.Read, Audit = AuditMode.Access)]
    Task<InboundMailItem> GetMailAsync(string teamKey, string mailId, CancellationToken cancellationToken = default);

    /// <summary>The mail as it arrived, or null when the team has none by this id.</summary>
    [RequireScope(MailboxScopes.Read, Audit = AuditMode.Access)]
    Task<MailContent> GetContentAsync(string teamKey, string mailId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what became of a mail and what it belongs to; a null <paramref name="outcome"/> returns it to the
    /// unhandled mail.
    /// </summary>
    [RequireScope(MailboxScopes.Handle, Audit = AuditMode.Change)]
    Task<InboundMailItem> SetOutcomeAsync(string teamKey, string mailId, string outcome, MailThreadOwner owner = null, CancellationToken cancellationToken = default);
}
