using MimeKit;
using Tharga.Team.Email;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// A mail with HTML is <c>multipart/alternative</c> with the text first; images the HTML names travel beside it in
/// <c>multipart/related</c>, and every other file after both in <c>multipart/mixed</c>.
/// </summary>
/// <remarks>
/// <b>The order inside the alternative matters.</b> A mail client shows the last alternative it understands, so the
/// text goes first and the HTML last; the other way round, every client that reads HTML would show the text.
/// </remarks>
public class OutboundHtmlTests
{
    private const string Html = "<p>Here it <b>is</b>.</p>";

    private static MailOptions Options()
    {
        var options = new MailOptions { FromAddress = "support@example.org", FromName = "Support" };
        options.Smtp.Host = "smtp.example.com";

        return options;
    }

    private static MimeMessage Create(string html, params MailAttachment[] attachments)
        => MailMessageFactory.Create(new OutboundMail("user@example.com", "Re: Export", "Here it is.", Attachments: attachments, BodyHtml: html), Options());

    [Fact]
    public void WithHtml_TheMailIsAlternative_TextFirst()
    {
        var message = Create(Html);

        var alternative = Assert.IsType<MultipartAlternative>(message.Body);
        Assert.Equal(2, alternative.Count);
        Assert.Equal("plain", Assert.IsType<TextPart>(alternative[0]).ContentType.MediaSubtype);
        Assert.Equal("html", Assert.IsType<TextPart>(alternative[1]).ContentType.MediaSubtype);
        Assert.Equal("Here it is.", message.TextBody);
        Assert.Equal(Html, message.HtmlBody);
    }

    [Fact]
    public void WithoutHtml_TheMailIsPlainTextAsBefore()
    {
        Assert.IsType<TextPart>(Create(null).Body);
        Assert.IsType<TextPart>(Create("  ").Body);
    }

    [Fact]
    public void WithHtmlAndFiles_TheAlternativeComesFirst_InMixed()
    {
        var message = Create(Html, new MailAttachment("report.pdf", "application/pdf", [1, 2]));

        var mixed = Assert.IsType<Multipart>(message.Body);
        Assert.Equal("mixed", mixed.ContentType.MediaSubtype);
        Assert.IsType<MultipartAlternative>(mixed[0]);
        var file = Assert.IsType<MimePart>(mixed[1]);
        Assert.Equal("report.pdf", file.FileName);
        Assert.True(file.IsAttachment);
    }

    [Fact]
    public void AnImageTheHtmlNames_TravelsInline_InRelated()
    {
        var message = Create("<p>Look: <img src=\"cid:chart@example.org\"></p>",
            new MailAttachment("chart.png", "image/png", [4, 5], "chart@example.org"),
            new MailAttachment("report.pdf", "application/pdf", [1]));

        var mixed = Assert.IsType<Multipart>(message.Body);
        var alternative = Assert.IsType<MultipartAlternative>(mixed[0]);
        var related = Assert.IsType<MultipartRelated>(alternative[1]);
        Assert.Equal("html", Assert.IsType<TextPart>(related[0]).ContentType.MediaSubtype);

        var image = Assert.IsType<MimePart>(related[1]);
        Assert.Equal("chart@example.org", image.ContentId);
        Assert.Equal(ContentDisposition.Inline, image.ContentDisposition.Disposition);

        Assert.Equal(["report.pdf"], message.Attachments.OfType<MimePart>().Select(x => x.FileName).ToArray());
    }

    [Fact]
    public void AFileWithAContentIdTheHtmlDoesNotName_IsAnOrdinaryAttachment()
    {
        var message = Create(Html, new MailAttachment("chart.png", "image/png", [4, 5], "chart@example.org"));

        var mixed = Assert.IsType<Multipart>(message.Body);
        Assert.IsType<TextPart>(Assert.IsType<MultipartAlternative>(mixed[0])[1]);
        Assert.True(Assert.IsType<MimePart>(mixed[1]).IsAttachment);
    }

    [Fact]
    public void OnlyInlineImages_NeedNoMixed()
    {
        var message = Create("<p><img src=\"cid:a@x\"></p>", new MailAttachment("a.png", "image/png", [1], "<a@x>"));

        var alternative = Assert.IsType<MultipartAlternative>(message.Body);
        Assert.IsType<MultipartRelated>(alternative[1]);
    }

    /// <summary>Written and parsed again, as the recipient's mail client will.</summary>
    [Fact]
    public void TheWrittenMail_ReadsBackWithBothBodiesAndItsImage()
    {
        var message = Create("<p>Look: <img src=\"cid:chart@example.org\"></p>", new MailAttachment("chart.png", "image/png", [4, 5], "chart@example.org"));

        using var written = new MemoryStream();
        message.WriteTo(written);
        written.Position = 0;

        var reread = InboundMailReader.Read(MimeMessage.Load(written));

        Assert.Equal("Here it is.", reread.Body);
        Assert.Contains("cid:chart@example.org", reread.BodyHtml);
        var image = Assert.Single(reread.Attachments);
        Assert.True(image.Inline);
        Assert.Equal("chart@example.org", image.ContentId);
        Assert.Equal([4, 5], image.Content);
    }
}
