namespace Tharga.Team.Email;

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
    /// Whether replies are invited to a per-case address, <c>support+{caseId}@…</c>. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <b>Off by default because the cost of it being wrong is asymmetric.</b> A mail server that does not
    /// accept plus-addressed local parts rejects every reply, and the sender gets a bounce — a failure in
    /// front of a customer. With it off, a reply is matched on its threading headers instead; the rare mail
    /// client that strips those leaves a reply unmatched, which is logged and visible to support rather than
    /// bounced back at the person who wrote it.
    /// <para>
    /// Turn it on once the mailbox is known to accept plus-addressing. It is the more robust of the two
    /// mechanisms, because a threading header can be dropped by anything in the path.
    /// </para>
    /// </remarks>
    public bool PerCaseReplyTo { get; set; }

    /// <summary>
    /// Domains or full addresses this instance accepts mail for. Empty accepts everything.
    /// </summary>
    /// <remarks>
    /// <b>For one mailbox serving more than one site.</b> Two products can share a mailbox — one receiving
    /// <c>example.com</c> and the other <c>example.org</c> — with each instance listing only its own and
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

    /// <summary>Where a mailbox with no read position starts: only mail delivered on or after this date. Unset reads everything.</summary>
    /// <remarks>
    /// <b>Set it when connecting a mailbox that already holds mail</b>, or the first reads turn every message it
    /// has ever received into work. It applies whenever reading starts from the beginning, which includes the
    /// server announcing a new UID generation.
    /// </remarks>
    public DateTime? FirstReadSince { get; set; }

    /// <summary>The largest file one mail may carry in or out, in bytes. Default 25 MB.</summary>
    public long MaxAttachmentBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>The most all files on one mail may add up to, in or out, in bytes. Default 50 MB.</summary>
    public long MaxAttachmentTotalBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>The most files one mail may carry in or out. Default 20.</summary>
    public int MaxAttachmentCount { get; set; } = 20;
}
