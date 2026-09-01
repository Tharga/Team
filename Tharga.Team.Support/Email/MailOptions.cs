namespace Tharga.Team.Support.Email;

/// <summary>
/// How the support module reads and sends mail.
/// </summary>
/// <remarks>
/// <b>Leave <see cref="MailServerOptions.Host"/> unset on both and nothing happens.</b> That is the ordinary
/// state for a host that never wanted email, exactly as an unset Slack channel is — not a degraded mode.
/// <para>
/// <b>Reading is a poll, not a push.</b> There is no endpoint to expose and nothing to sign: the mailbox
/// credentials are the whole trust boundary of the transport. The cost is latency — a reply appears at the
/// next poll rather than when it was sent — and <see cref="PollInterval"/> is the dial for it.
/// </para>
/// </remarks>
public class MailOptions
{
    /// <summary>Mailbox to read replies from.</summary>
    public MailServerOptions Imap { get; } = new();

    /// <summary>Server to send through.</summary>
    public MailServerOptions Smtp { get; } = new();

    /// <summary>Address mail is sent from, and that replies are expected back at.</summary>
    public string FromAddress { get; set; }

    /// <summary>Display name on outgoing mail.</summary>
    public string FromName { get; set; }

    /// <summary>Mailbox folder to read. Default <c>INBOX</c>.</summary>
    public string Folder { get; set; } = "INBOX";

    /// <summary>How often the mailbox is read. Default one minute.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How long a single server operation may take before it is abandoned. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Domains or full addresses this instance accepts mail for. Empty accepts everything.
    /// </summary>
    /// <remarks>
    /// <b>For one mailbox serving more than one site.</b> Two products can share a mailbox — one receiving
    /// <c>fortdocs.se</c> and the other <c>eplicta.se</c> — with each instance listing only its own and
    /// ignoring the rest. A bare domain matches any local part; a full address matches only itself.
    /// <para>
    /// <b>Set this and the sending address must be covered by it</b>, or the instance discards every reply to
    /// its own mail while looking exactly like a mailbox that is not being read. Registration checks the two
    /// agree and refuses to start when they do not.
    /// </para>
    /// <para>
    /// <b>Reliability depends on the receiving mail server.</b> Which recipient a mail was addressed to is
    /// read from <c>Delivered-To</c>, <c>X-Original-To</c> or <c>Envelope-To</c>, and which of those exists
    /// is the receiving server's choice — IMAP exposes no envelope. Where none is present the filter can only
    /// fall back to <c>To</c> and <c>Cc</c>, which say nothing about a bcc'd or forwarded mail. Verify
    /// against the real mailbox before relying on this to separate two sites.
    /// </para>
    /// </remarks>
    public string[] Recipients { get; set; } = [];
}
