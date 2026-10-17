using MimeKit;
using MimeKit.Utils;

namespace Tharga.Team.Email;

/// <summary>
/// Builds the MIME message for an <see cref="OutboundMail"/>.
/// </summary>
/// <remarks>
/// Separated from the SMTP client so the headers that make threading work can be asserted without a server.
/// They are the part that silently fails: a mail with the right body and no <c>References</c> is delivered
/// perfectly and lands as an unrelated message in the recipient's inbox.
/// </remarks>
internal static class MailMessageFactory
{
    public static MimeMessage Create(OutboundMail mail, MailOptions options)
    {
        var from = new MailboxAddress(options.FromName ?? string.Empty, options.FromAddress);

        var message = new MimeMessage
        {
            Subject = mail.Subject ?? string.Empty,
            Body = Body(mail)
        };

        message.From.Add(from);
        message.To.Add(MailboxAddress.Parse(mail.To));

        // Generated here rather than read back from the server, because SMTP does not report one. The caller
        // stores it as the thread's identity, and the reply names it in In-Reply-To.
        message.MessageId = MimeUtils.GenerateMessageId(from.Domain);

        if (!string.IsNullOrWhiteSpace(mail.ReplyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(mail.ReplyTo));
        }

        if (!string.IsNullOrWhiteSpace(mail.InReplyTo))
        {
            message.InReplyTo = mail.InReplyTo;
        }

        foreach (var reference in References(mail))
        {
            message.References.Add(reference);
        }

        return message;
    }

    /// <summary>
    /// The text alone, or with HTML as <c>multipart/alternative</c>; inside <c>multipart/mixed</c> with each file
    /// after it in order when there are files.
    /// </summary>
    /// <remarks>
    /// Every file goes base64 under its declared type, so nothing in it is read as part of the message; a type that
    /// does not parse goes as <c>application/octet-stream</c>. A file the HTML names by <c>cid:</c> goes inline in
    /// <c>multipart/related</c> beside the HTML, so a mail client shows it in place rather than listing it.
    /// </remarks>
    private static MimeEntity Body(OutboundMail mail)
    {
        var named = string.IsNullOrWhiteSpace(mail.BodyHtml) ? [] : HtmlContentIds.Of(mail.BodyHtml);
        var inline = new List<MailAttachment>();
        var attached = new List<MailAttachment>();

        foreach (var attachment in mail.Attachments ?? [])
        {
            var isNamed = !string.IsNullOrWhiteSpace(attachment.ContentId) && named.Contains(attachment.ContentId.Trim('<', '>'));
            (isNamed ? inline : attached).Add(attachment);
        }

        MimeEntity body = new TextPart("plain") { Text = mail.Body ?? string.Empty };

        if (!string.IsNullOrWhiteSpace(mail.BodyHtml)) body = new MultipartAlternative { body, Html(mail.BodyHtml, inline) };

        if (attached.Count == 0) return body;

        var mixed = new Multipart("mixed") { body };
        foreach (var attachment in attached) mixed.Add(Part(attachment, ContentDisposition.Attachment));

        return mixed;
    }

    private static MimeEntity Html(string html, IReadOnlyList<MailAttachment> inline)
    {
        var part = new TextPart("html") { Text = html };
        if (inline.Count == 0) return part;

        var related = new MultipartRelated { part };
        foreach (var image in inline) related.Add(Part(image, ContentDisposition.Inline));

        return related;
    }

    private static MimePart Part(MailAttachment attachment, string disposition)
    {
        var part = new MimePart(ContentType.TryParse(attachment.ContentType ?? string.Empty, out var parsed) ? parsed : new ContentType("application", "octet-stream"))
        {
            Content = new MimeContent(new MemoryStream(attachment.Content ?? [], false)),
            ContentDisposition = new ContentDisposition(disposition),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = AttachmentFileName.Sanitize(attachment.FileName, attachment.ContentType)
        };

        if (disposition == ContentDisposition.Inline) part.ContentId = attachment.ContentId.Trim('<', '>');

        return part;
    }

    /// <summary>
    /// The thread chain to send, oldest first, with the message being replied to last.
    /// </summary>
    /// <remarks>
    /// <c>In-Reply-To</c> alone is not enough: clients thread on <c>References</c>, and a reply that names
    /// only its immediate parent starts a new conversation in some of them. The parent is appended when the
    /// caller did not already include it, so a caller passing the full chain is not punished for it.
    /// </remarks>
    private static IEnumerable<string> References(OutboundMail mail)
    {
        var chain = (mail.References ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (!string.IsNullOrWhiteSpace(mail.InReplyTo) && !chain.Contains(mail.InReplyTo))
        {
            chain.Add(mail.InReplyTo);
        }

        return chain;
    }
}
