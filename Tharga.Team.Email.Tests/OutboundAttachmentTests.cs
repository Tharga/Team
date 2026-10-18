using MimeKit;
using Tharga.Team.Email;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// A mail sent with files is <c>multipart/mixed</c>: the text first, then each file as a base64 attachment
/// under its declared type and a safe name.
/// </summary>
public class OutboundAttachmentTests
{
    private static MailOptions Options()
    {
        var options = new MailOptions { FromAddress = "support@example.org", FromName = "Support" };
        options.Smtp.Host = "smtp.example.com";

        return options;
    }

    private static MimeMessage Create(params MailAttachment[] attachments)
        => MailMessageFactory.Create(new OutboundMail("user@example.com", "Re: Export", "Here it is.", Attachments: attachments), Options());

    [Fact]
    public void WithoutFiles_TheMailIsPlainText()
    {
        var message = Create();

        Assert.IsType<TextPart>(message.Body);
    }

    [Fact]
    public void WithFiles_TheMailIsMixed_TextFirst_FilesInOrder()
    {
        var message = Create(
            new MailAttachment("report.pdf", "application/pdf", [1, 2, 3]),
            new MailAttachment("chart.png", "image/png", [4, 5]));

        var mixed = Assert.IsType<Multipart>(message.Body);
        Assert.Equal("mixed", mixed.ContentType.MediaSubtype);
        Assert.Equal("Here it is.", Assert.IsType<TextPart>(mixed[0]).Text);
        Assert.Equal("Here it is.", message.TextBody);

        var files = message.Attachments.OfType<MimePart>().ToArray();
        Assert.Equal(["report.pdf", "chart.png"], files.Select(x => x.FileName).ToArray());
        Assert.Equal(["application/pdf", "image/png"], files.Select(x => x.ContentType.MimeType).ToArray());
        Assert.All(files, x => Assert.Equal(ContentEncoding.Base64, x.ContentTransferEncoding));
        Assert.All(files, x => Assert.True(x.IsAttachment));
    }

    [Fact]
    public void AFile_TravelsByteForByte()
    {
        var bytes = Enumerable.Range(0, 1000).Select(x => (byte)x).ToArray();

        var file = Create(new MailAttachment("data.bin", "application/octet-stream", bytes)).Attachments.OfType<MimePart>().Single();

        using var decoded = new MemoryStream();
        file.Content.DecodeTo(decoded);
        Assert.Equal(bytes, decoded.ToArray());
    }

    [Fact]
    public void ATypeThatDoesNotParse_GoesAsOctetStream()
    {
        var file = Create(new MailAttachment("odd.bin", "not a type", [1])).Attachments.OfType<MimePart>().Single();

        Assert.Equal("application/octet-stream", file.ContentType.MimeType);
    }

    [Fact]
    public void AFileName_IsSanitizedOnTheWayOut()
    {
        var file = Create(new MailAttachment(@"..\secret\report.pdf", "application/pdf", [1])).Attachments.OfType<MimePart>().Single();

        Assert.Equal("report.pdf", file.FileName);
    }

    /// <summary>The whole message is written and parsed again, as the recipient's mail client will.</summary>
    [Fact]
    public void TheWrittenMail_ReadsBackWithItsFiles()
    {
        var message = Create(new MailAttachment("rapport ärende.pdf", "application/pdf", [7, 7]));

        using var written = new MemoryStream();
        message.WriteTo(written);
        written.Position = 0;

        var reread = InboundMailReader.Read(MimeMessage.Load(written));

        var file = Assert.Single(reread.Attachments);
        Assert.Equal("rapport ärende.pdf", file.FileName);
        Assert.Equal([7, 7], file.Content);
        Assert.Equal("Here it is.", reread.Body);
    }
}
