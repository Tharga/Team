namespace Tharga.Team.Support.Email;

/// <summary>
/// A mail to send.
/// </summary>
/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="Body">Plain-text body.</param>
/// <param name="InReplyTo">
/// <c>Message-ID</c> of the mail being replied to, or null to start a thread.
/// </param>
/// <param name="References">
/// The thread's existing <c>Message-ID</c> chain, oldest first. Mail clients thread on this, not on the
/// subject, so dropping it turns a conversation into a pile of unrelated mails in the recipient's inbox.
/// </param>
/// <param name="ReplyTo">
/// Address replies should be sent to, when it differs from the sender — a per-case
/// <c>support+{caseId}@…</c>. Null uses the configured sending address.
/// </param>
public readonly record struct OutboundMail(
    string To,
    string Subject,
    string Body,
    string InReplyTo = null,
    IReadOnlyList<string> References = null,
    string ReplyTo = null);

/// <summary>
/// What happened to a send.
/// </summary>
/// <param name="Success">True when the server accepted the mail.</param>
/// <param name="Error">Why it did not, or null on success.</param>
/// <param name="MessageId">
/// The <c>Message-ID</c> the mail was sent with, or null when the send failed.
/// </param>
/// <remarks>
/// <b><see cref="MessageId"/> is what makes a thread possible</b>, and it is generated before sending rather
/// than reported by the server — SMTP does not return one. A caller that wants a conversation keeps it; the
/// reply comes back naming it in <c>In-Reply-To</c>.
/// <para>
/// <b>Acceptance is not delivery.</b> A bounce arrives minutes later as a separate mail, so a successful
/// result means the server took responsibility for the message, not that anyone received it.
/// </para>
/// </remarks>
public readonly record struct MailSendResult(bool Success, string Error, string MessageId = null)
{
    /// <summary>The server accepted the mail.</summary>
    public static MailSendResult Ok(string messageId) => new(true, null, messageId);

    /// <summary>It did not, for the stated reason.</summary>
    public static MailSendResult Failed(string error) => new(false, error);
}

/// <summary>
/// How far a mailbox has been read.
/// </summary>
/// <param name="UidValidity">
/// The mailbox's UID generation. When a server changes this, every stored UID refers to a different message
/// and the position must be discarded rather than trusted.
/// </param>
/// <param name="LastUid">The highest UID already considered. Zero starts from the beginning.</param>
/// <remarks>
/// <b>A position, not a flag.</b> Two applications may read one mailbox, so "handled" cannot be recorded in
/// shared mailbox state — each keeps its own position and ignores what is not addressed to it.
/// </remarks>
public readonly record struct MailFetchPosition(uint UidValidity, uint LastUid)
{
    /// <summary>Nothing has been read yet.</summary>
    public static MailFetchPosition Start => new(0, 0);

    /// <summary>Whether a mailbox reporting <paramref name="uidValidity"/> invalidates this position.</summary>
    public bool IsInvalidatedBy(uint uidValidity) => UidValidity != 0 && UidValidity != uidValidity;
}

/// <summary>
/// What one read of the mailbox produced.
/// </summary>
/// <param name="Position">Where to resume from next time.</param>
/// <param name="Mails">What arrived, oldest first.</param>
/// <param name="Rescanned">
/// True when the mailbox's UID generation had changed and the position was discarded, so these may include
/// mail already seen. Deduplication downstream is what makes that safe.
/// </param>
public readonly record struct MailFetchResult(
    MailFetchPosition Position,
    IReadOnlyList<InboundMail> Mails,
    bool Rescanned = false)
{
    /// <summary>Nothing was read, and the position is unchanged.</summary>
    public static MailFetchResult Empty(MailFetchPosition position) => new(position, []);
}

/// <summary>
/// A mail that arrived.
/// </summary>
/// <param name="MessageId">The mail's own <c>Message-ID</c>. The identity a redelivery is recognised by.</param>
/// <param name="From">Sender address, lower-cased and stripped of any display name.</param>
/// <param name="DeliveredTo">
/// Every address this mail can be shown to have been addressed to, most trustworthy first.
/// </param>
/// <param name="Subject">Subject line.</param>
/// <param name="Body">Plain-text body, taken from the text part or flattened from HTML.</param>
/// <param name="SentAt">When the sender says it was sent.</param>
/// <param name="InReplyTo">The <c>Message-ID</c> this is a reply to, or null.</param>
/// <param name="References">The thread's <c>Message-ID</c> chain, oldest first.</param>
/// <param name="IsAutomated">
/// True when the mail announces itself as machine-generated — a vacation responder, a bounce, a bulk send.
/// </param>
/// <param name="HadAttachments">
/// True when attachments were present. They are not carried: storing them needs somewhere to put them, and
/// silently dropping them without saying so would leave a transcript that misrepresents the conversation.
/// </param>
public readonly record struct InboundMail(
    string MessageId,
    string From,
    IReadOnlyList<string> DeliveredTo,
    string Subject,
    string Body,
    DateTimeOffset SentAt,
    string InReplyTo = null,
    IReadOnlyList<string> References = null,
    bool IsAutomated = false,
    bool HadAttachments = false);
