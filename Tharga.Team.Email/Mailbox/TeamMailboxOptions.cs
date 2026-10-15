namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// How a team's mail address is made, and how long a replaced key keeps working.
/// </summary>
public class TeamMailboxOptions
{
    /// <summary>The domain every team address is on, e.g. <c>fortdocs.app</c>. The catch-all mailbox receives the whole domain.</summary>
    public string Domain { get; set; }

    /// <summary>Written after the key in the local part, e.g. <c>-case</c> for <c>{key}-case@…</c>; empty for none.</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>How many characters a key has. Twelve lowercase letters and digits is about 62 bits.</summary>
    public int KeyLength { get; set; } = 12;

    /// <summary>
    /// The catch-all mailbox every team address lands in. Mail is read only when <see cref="MailOptions.Imap"/>
    /// is configured; the mailbox is only ever read, never changed.
    /// </summary>
    public MailOptions Mail { get; set; } = new();

    /// <summary>How long a replaced key is still accepted after a rotation.</summary>
    public TimeSpan RetirementPeriod { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How long the postmaster keeps mail no team owns before deleting it, with its content.</summary>
    public TimeSpan PostmasterRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// The lowest access level that holds <see cref="MailboxScopes.Read"/>; <c>null</c> registers it grant-only,
    /// so only a role or an override confers it.
    /// </summary>
    public AccessLevel? ReadAccessLevel { get; set; } = AccessLevel.User;

    /// <summary>The lowest access level that holds <see cref="MailboxScopes.Handle"/>; <c>null</c> for grant-only.</summary>
    public AccessLevel? HandleAccessLevel { get; set; } = AccessLevel.User;

    /// <summary>The lowest access level that holds <see cref="MailboxScopes.Manage"/>; <c>null</c> for grant-only.</summary>
    public AccessLevel? ManageAccessLevel { get; set; } = AccessLevel.Administrator;
}
