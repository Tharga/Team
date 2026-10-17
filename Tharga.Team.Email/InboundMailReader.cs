using MimeKit;

namespace Tharga.Team.Email;

/// <summary>
/// Turns a received MIME message into an <see cref="InboundMail"/>.
/// </summary>
/// <remarks>
/// Separated from the IMAP client so every one of these decisions can be asserted against a constructed
/// message rather than a live mailbox.
/// </remarks>
internal static class InboundMailReader
{
    /// <summary>
    /// Headers naming the address a mail was actually delivered to, most trustworthy first.
    /// </summary>
    /// <remarks>
    /// <b>IMAP exposes no envelope, so this chain is the whole mechanism</b> — and which of these a mailbox
    /// carries is decided by the receiving mail server, not by anything here. <c>To</c> and <c>Cc</c> come
    /// last and are a guess: they can name a list, hold several addresses, or say nothing at all about a mail
    /// that was bcc'd or forwarded.
    /// </remarks>
    private static readonly string[] DeliveryHeaders = ["Delivered-To", "X-Original-To", "Envelope-To"];

    /// <summary>
    /// Headers by which a mail declares itself machine-generated. Any of them is enough.
    /// </summary>
    /// <remarks>
    /// Answering one of these is how a support case and a vacation responder fill each other's mailboxes
    /// overnight. <c>Precedence</c> is checked by value because <c>Precedence: bulk</c> marks automation
    /// while other values do not.
    /// </remarks>
    private static readonly string[] AutomationHeaders = ["Auto-Submitted", "X-Auto-Response-Suppress", "List-Id", "List-Unsubscribe"];

    private static readonly string[] AutomatedPrecedence = ["bulk", "junk", "list", "auto_reply"];

    public static InboundMail Read(MimeMessage message, uint uid = 0, byte[] raw = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var bounce = BounceReader.Read(message);

        // A bounce is news about an answer, never a case, so what it carries -- usually the answer itself -- is not read.
        IReadOnlyList<InboundAttachment> attachments = bounce == null ? Attachments(message) : [];

        return new InboundMail(
            MessageId: message.MessageId,
            From: Sender(message),
            DeliveredTo: DeliveredTo(message),
            Subject: message.Subject ?? string.Empty,
            Body: Body(message),
            SentAt: message.Date,
            InReplyTo: message.InReplyTo,
            References: [.. message.References],
            IsAutomated: bounce != null || IsAutomated(message),
            HadAttachments: attachments.Count > 0 || message.Attachments.Any(),
            Uid: uid,
            Raw: raw,
            FromName: message.From.Mailboxes.FirstOrDefault()?.Name,
            To: [.. message.To.Mailboxes.Select(x => Normalize(x.Address)).Where(x => x.Length > 0)],
            Cc: [.. message.Cc.Mailboxes.Select(x => Normalize(x.Address)).Where(x => x.Length > 0)],
            BouncedMessageId: bounce?.MessageId,
            BounceReason: bounce?.Reason,
            Attachments: attachments);
    }

    /// <summary>
    /// Every file in the mail, in the order it appears: attachments, images shown inside the body, and forwarded
    /// messages as <c>.eml</c> files.
    /// </summary>
    /// <remarks>
    /// <b>The text the body was read from is not a file</b>, and neither is a signature part: a detached
    /// signature means nothing once separated from what it signed. A plain or HTML text part counts only when
    /// the sender marked it as an attachment or named it.
    /// </remarks>
    internal static IReadOnlyList<InboundAttachment> Attachments(MimeMessage message)
    {
        var attachments = new List<InboundAttachment>();

        foreach (var entity in message.BodyParts)
        {
            switch (entity)
            {
                case MessagePart forwarded:
                    attachments.Add(Forwarded(forwarded));
                    break;
                case MimePart part when IsFile(part):
                    attachments.Add(File(part));
                    break;
            }
        }

        return attachments;
    }

    private static readonly string[] SignatureTypes = ["application/pkcs7-signature", "application/x-pkcs7-signature", "application/pgp-signature"];

    private static bool IsFile(MimePart part)
    {
        if (SignatureTypes.Contains(part.ContentType.MimeType, StringComparer.OrdinalIgnoreCase)) return false;

        return part.IsAttachment
               || !string.IsNullOrWhiteSpace(part.FileName)
               || part is not TextPart { IsPlain: true } and not TextPart { IsHtml: true };
    }

    private static InboundAttachment File(MimePart part)
    {
        var contentType = AttachmentContentType.Of(part.ContentType);

        using var content = new MemoryStream();
        part.Content?.DecodeTo(content);

        var inline = !part.IsAttachment && (part.ContentDisposition != null || !string.IsNullOrWhiteSpace(part.ContentId));

        return new InboundAttachment(
            AttachmentFileName.Sanitize(part.FileName, contentType),
            contentType,
            content.ToArray(),
            inline,
            string.IsNullOrWhiteSpace(part.ContentId) ? null : part.ContentId.Trim().Trim('<', '>'));
    }

    /// <remarks>
    /// Kept whole, headers and all, because that is what makes a forwarded mail worth having: who sent it, when,
    /// and to whom. Named after its subject when the sender gave it no name.
    /// </remarks>
    private static InboundAttachment Forwarded(MessagePart part)
    {
        using var content = new MemoryStream();
        part.Message?.WriteTo(content);

        var name = part.ContentDisposition?.FileName ?? part.ContentType.Name ?? part.Message?.Subject;

        return new InboundAttachment(
            AttachmentFileName.SanitizeWithExtension(name, ForwardedType, ".eml"),
            ForwardedType,
            content.ToArray());
    }

    private const string ForwardedType = "message/rfc822";

    /// <summary>
    /// Reads as <see cref="Read"/> does, and records whether <paramref name="verifier"/> finds the sender
    /// verified; a null verifier leaves it unverified.
    /// </summary>
    public static async Task<InboundMail> ReadAsync(MimeMessage message, DkimSenderVerifier verifier, uint uid = 0, byte[] raw = null, CancellationToken cancellationToken = default)
    {
        var mail = Read(message, uid, raw);

        var domain = verifier == null ? null : await verifier.VerifiedDomainAsync(message, cancellationToken);

        return domain == null ? mail : mail with { SenderVerified = true, SenderDomain = domain };
    }

    private static string Sender(MimeMessage message)
        => Normalize(message.From.Mailboxes.FirstOrDefault()?.Address);

    private static string[] DeliveredTo(MimeMessage message)
    {
        var addresses = DeliveryHeaders
            .SelectMany(header => message.Headers.Where(x => string.Equals(x.Field, header, StringComparison.OrdinalIgnoreCase)))
            .Select(x => Normalize(AddressOf(x.Value)))
            .ToList();

        addresses.AddRange(message.To.Mailboxes.Concat(message.Cc.Mailboxes).Select(x => Normalize(x.Address)));

        return [.. addresses.Where(x => x.Length > 0).Distinct()];
    }

    /// <summary>
    /// A delivery header may be a bare address or a full mailbox with a display name.
    /// </summary>
    private static string AddressOf(string headerValue)
        => MailboxAddress.TryParse(headerValue, out var mailbox) ? mailbox.Address : headerValue;

    private static string Normalize(string address)
        => address?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsEmptyReturnPath(string value)
    {
        var text = value?.Trim();

        return string.IsNullOrEmpty(text) || text == "<>";
    }

    /// <summary>
    /// The plain-text body, flattening HTML when that is all the sender provided.
    /// </summary>
    /// <remarks>
    /// <b>Quoted history and signatures are not removed here.</b> That is a decision about what belongs in a
    /// transcript, not about what the mail contained, and it lives with the case rather than the transport.
    /// </remarks>
    private static string Body(MimeMessage message)
    {
        var text = message.TextBody;

        return !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : HtmlText.ToPlainText(message.HtmlBody);
    }

    private static bool IsAutomated(MimeMessage message)
    {
        // An empty return path is how a bounce identifies itself, and answering one loops with the far mail
        // server rather than with a person. It is written literally as <>, which parses as no address rather
        // than as an empty string.
        if (message.Headers.Contains(HeaderId.ReturnPath) && IsEmptyReturnPath(message.Headers[HeaderId.ReturnPath]))
            return true;

        foreach (var header in message.Headers)
        {
            if (AutomationHeaders.Contains(header.Field, StringComparer.OrdinalIgnoreCase))
            {
                // Auto-Submitted: no is the explicit way of saying a human sent it.
                if (string.Equals(header.Field, "Auto-Submitted", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(header.Value?.Trim(), "no", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return true;
            }

            if (string.Equals(header.Field, "Precedence", StringComparison.OrdinalIgnoreCase) &&
                AutomatedPrecedence.Contains(header.Value?.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
