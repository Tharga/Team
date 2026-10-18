using System.Globalization;
using MimeKit;
using Tharga.Team.Service.Html;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Reads a kept raw message for a person: its text, its formatting sanitized, the images it shows in place, and its
/// files by position.
/// </summary>
/// <remarks>
/// <b>Every file is found the way support finds them</b> — attachments, inline images, forwarded mail as
/// <c>.eml</c>, signatures left out — so a file's position, which is its id, is the same on every read of the same
/// message. Nothing is cut: a postmaster reads the whole mail, quoted history and all, since the point is to find
/// out whose it is.
/// </remarks>
internal static class ReceivedMailReader
{
    public static ReceivedMailBody Body(string mailId, byte[] raw)
    {
        var message = Load(raw);
        if (message == null) return new ReceivedMailBody { MailId = mailId, Text = string.Empty };

        var files = InboundMailReader.Attachments(message);
        var html = SafeHtml.Sanitize(message.HtmlBody);
        var named = html == null ? [] : SafeHtml.ContentIds(html);

        return new ReceivedMailBody
        {
            MailId = mailId,
            Text = TextOf(message),
            Html = html,
            Attachments = [.. files.Select(Describe)],
            Images = [.. files
                .Where(x => x.ContentId != null && named.Contains(x.ContentId) && SafeHtml.ImageMediaType(x.ContentType) != null && x.Content.Length <= ReceivedMailBody.MaxInlineImageBytes)
                .DistinctBy(x => x.ContentId)
                .Select(x => new ReceivedMailImage { ContentId = x.ContentId, ContentType = SafeHtml.ImageMediaType(x.ContentType), Content = x.Content })]
        };
    }

    /// <summary>The file at <paramref name="attachmentId"/>, a position counted from 1; null for an id that names none.</summary>
    public static SupportAttachmentContent Open(byte[] raw, string attachmentId)
    {
        if (!int.TryParse(attachmentId, NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position < 1) return null;

        var message = Load(raw);
        if (message == null) return null;

        var files = InboundMailReader.Attachments(message);
        if (position > files.Count) return null;

        var file = files[position - 1];
        return new SupportAttachmentContent { Attachment = Describe(file, position - 1), Content = new MemoryStream(file.Content, writable: false) };
    }

    private static SupportAttachment Describe(InboundAttachment file, int index) => new()
    {
        Id = (index + 1).ToString(CultureInfo.InvariantCulture),
        FileName = file.FileName,
        ContentType = file.ContentType,
        Size = file.Content.LongLength,
        Inline = file.Inline,
        ContentId = file.ContentId
    };

    private static string TextOf(MimeMessage message)
    {
        var text = message.TextBody;

        return !string.IsNullOrWhiteSpace(text) ? text.Trim() : HtmlText.ToPlainText(message.HtmlBody);
    }

    /// <remarks>A message that does not parse reads as one with nothing in it, rather than failing the reader's page.</remarks>
    private static MimeMessage Load(byte[] raw)
    {
        if (raw is not { Length: > 0 }) return null;

        try
        {
            using var stream = new MemoryStream(raw, writable: false);
            return MimeMessage.Load(stream);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
