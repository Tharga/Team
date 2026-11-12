namespace Tharga.Team.Email;

/// <summary>An address a mailbox's SMTP account may send as, besides its own <see cref="MailOptions.FromAddress"/>.</summary>
public sealed class MailSendAs
{
    /// <summary>The address mail is sent from.</summary>
    public string Address { get; set; }

    /// <summary>The name the recipient sees beside the address; null uses the mailbox's <see cref="MailOptions.FromName"/>.</summary>
    public string Name { get; set; }
}
