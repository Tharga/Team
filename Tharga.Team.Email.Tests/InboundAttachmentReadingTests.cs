using System.Text;
using MimeKit;
using Tharga.Team.Email;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// The files a received mail carries: attachments, images shown inside an HTML body, and forwarded mails.
/// </summary>
/// <remarks>
/// <b>The inline images are the ones easy to lose.</b> They sit in <c>multipart/related</c> without an attachment
/// disposition, so a reader that asks only for attachments never sees them — and a later HTML body naming them
/// as <c>cid:</c> would then point at nothing.
/// </remarks>
public class InboundAttachmentReadingTests
{
    private static MimeMessage Message(MimeEntity body)
    {
        var message = new MimeMessage
        {
            Subject = "Export is empty",
            Body = body,
            MessageId = "first-1@example.com",
            Date = new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero)
        };

        message.From.Add(new MailboxAddress("A User", "user@example.com"));
        message.To.Add(new MailboxAddress("Support", "support@example.org"));

        return message;
    }

    private static MimePart File(string mediaType, string mediaSubtype, byte[] content, string fileName = null, string disposition = ContentDisposition.Attachment, string contentId = null)
    {
        var part = new MimePart(mediaType, mediaSubtype)
        {
            Content = new MimeContent(new MemoryStream(content)),
            ContentTransferEncoding = ContentEncoding.Base64
        };

        if (disposition != null) part.ContentDisposition = new ContentDisposition(disposition);
        if (fileName != null) part.FileName = fileName;
        if (contentId != null) part.ContentId = contentId;

        return part;
    }

    [Fact]
    public void AnImageInsideAnHtmlBody_IsKeptAsInline_WithItsContentId()
    {
        var html = new TextPart("html") { Text = "<p>Look: <img src=\"cid:logo@example.com\"></p>" };
        var image = File("image", "png", [1, 2, 3], "logo.png", ContentDisposition.Inline, "logo@example.com");

        var mail = InboundMailReader.Read(Message(new MultipartRelated { html, image }));

        var attachment = Assert.Single(mail.Attachments);
        Assert.True(attachment.Inline);
        Assert.Equal("logo@example.com", attachment.ContentId);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal([1, 2, 3], attachment.Content);
    }

    [Fact]
    public void AnInlineImageWithNoDisposition_IsStillKept_BecauseItHasAContentId()
    {
        var html = new TextPart("html") { Text = "<img src=\"cid:chart@example.com\">" };
        var image = File("image", "jpeg", [9], disposition: null, contentId: "chart@example.com");

        var mail = InboundMailReader.Read(Message(new MultipartRelated { html, image }));

        var attachment = Assert.Single(mail.Attachments);
        Assert.True(attachment.Inline);
        Assert.Equal("chart@example.com", attachment.ContentId);
        Assert.Equal("attachment.jpg", attachment.FileName);
    }

    [Fact]
    public void TheTextTheBodyWasReadFrom_IsNoAttachment()
    {
        var alternative = new MultipartAlternative
        {
            new TextPart("plain") { Text = "Plain." },
            new TextPart("html") { Text = "<p>Plain.</p>" }
        };

        var mail = InboundMailReader.Read(Message(alternative));

        Assert.Empty(mail.Attachments);
        Assert.False(mail.HadAttachments);
    }

    [Fact]
    public void ATextFileTheSenderNamed_IsAnAttachment_EvenShownInline()
    {
        var body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Log below." },
            new TextPart("plain") { Text = "line 1", FileName = "app.log", ContentDisposition = new ContentDisposition(ContentDisposition.Inline) { FileName = "app.log" } }
        };

        var mail = InboundMailReader.Read(Message(body));

        Assert.Equal("app.log", Assert.Single(mail.Attachments).FileName);
        Assert.Equal("Log below.", mail.Body);
    }

    /// <summary>A forwarded mail is worth keeping whole: who sent it, when and to whom is the point of forwarding it.</summary>
    [Fact]
    public void AForwardedMail_IsKeptAsAnEmlFile_NamedAfterItsSubject()
    {
        var forwarded = Message(new TextPart("plain") { Text = "The original." });
        forwarded.Subject = "Invoice 42";

        var body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "See the forwarded mail." },
            new MessagePart { Message = forwarded }
        };

        var mail = InboundMailReader.Read(Message(body));

        var attachment = Assert.Single(mail.Attachments);
        Assert.Equal("Invoice 42.eml", attachment.FileName);
        Assert.Equal("message/rfc822", attachment.ContentType);

        var reread = MimeMessage.Load(new MemoryStream(attachment.Content));
        Assert.Equal("Invoice 42", reread.Subject);
        Assert.Equal("The original.", reread.TextBody.Trim());
    }

    [Fact]
    public void AForwardedMailWithNoSubject_StillGetsAnEmlName()
    {
        var forwarded = Message(new TextPart("plain") { Text = "No subject here." });
        forwarded.Headers.Remove(HeaderId.Subject);

        var mail = InboundMailReader.Read(Message(new Multipart("mixed") { new TextPart("plain") { Text = "Fwd." }, new MessagePart { Message = forwarded } }));

        Assert.Equal("attachment.eml", Assert.Single(mail.Attachments).FileName);
    }

    [Fact]
    public void ADetachedSignature_IsNoAttachment()
    {
        var body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Signed." },
            File("application", "pkcs7-signature", [1], "smime.p7s")
        };

        Assert.Empty(InboundMailReader.Read(Message(body)).Attachments);
    }

    [Fact]
    public void AFileName_LosesAnyPathItCarries()
    {
        var body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Here." },
            File("application", "pdf", [1], @"..\..\windows\report.pdf")
        };

        Assert.Equal("report.pdf", Assert.Single(InboundMailReader.Read(Message(body)).Attachments).FileName);
    }

    [Fact]
    public void TheDeclaredType_IsKeptAsData_WithoutTheNameParameter()
    {
        var part = File("text", "plain", "hej"u8.ToArray(), "notes.txt");
        part.ContentType.Charset = "ISO-8859-1";

        var mail = InboundMailReader.Read(Message(new Multipart("mixed") { new TextPart("plain") { Text = "Notes." }, part }));

        Assert.Equal("text/plain; charset=iso-8859-1", Assert.Single(mail.Attachments).ContentType);
    }

    [Fact]
    public void FilesAreCarried_InTheOrderTheyAppear()
    {
        var body = new Multipart("mixed")
        {
            new TextPart("plain") { Text = "Three." },
            File("application", "pdf", [1], "one.pdf"),
            File("image", "png", [2], "two.png"),
            File("application", "zip", [3], "three.zip")
        };

        Assert.Equal(["one.pdf", "two.png", "three.zip"], InboundMailReader.Read(Message(body)).Attachments.Select(x => x.FileName).ToArray());
    }

    /// <summary>A bounce is news about an answer, never a case; what it carries is usually the answer itself.</summary>
    [Fact]
    public void ABounce_CarriesNoFiles()
    {
        var bounce = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(BounceReaderTests.EximReport)));

        var mail = InboundMailReader.Read(bounce);

        Assert.NotNull(mail.BouncedMessageId);
        Assert.Empty(mail.Attachments);
    }
}
