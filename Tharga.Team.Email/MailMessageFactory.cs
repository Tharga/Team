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
    /// The text alone, or <c>multipart/mixed</c> with the text first and each file after it in order.
    /// </summary>
    /// <remarks>
    /// Every file goes base64 as an attachment under its declared type, so nothing in it is read as part of the
    /// message; a type that does not parse goes as <c>application/octet-stream</c>.
    /// </remarks>
    private static MimeEntity Body(OutboundMail mail)
    {
        var text = new TextPart("plain") { Text = mail.Body ?? string.Empty };

        if (mail.Attachments is not { Count: > 0 } attachments) return text;

        var mixed = new Multipart("mixed") { text };

        foreach (var attachment in attachments)
        {
            mixed.Add(new MimePart(ContentType.TryParse(attachment.ContentType ?? string.Empty, out var parsed) ? parsed : new ContentType("application", "octet-stream"))
            {
                Content = new MimeContent(new MemoryStream(attachment.Content ?? [], false)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = AttachmentFileName.Sanitize(attachment.FileName, attachment.ContentType)
            });
        }

        return mixed;
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
