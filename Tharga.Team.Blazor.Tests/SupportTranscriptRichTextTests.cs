using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The transcript shows an entry's formatting only through the sanitizer, shows the images it names in place from
/// content read through the service, and lists every file that is not shown that way.
/// </summary>
public class SupportTranscriptRichTextTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
    private const string Author = "alice";

    private readonly Mock<ISupportCaseService> _cases = new();

    private static SupportMessage Entry(string html, string body = "Plain", params SupportAttachment[] attachments) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = Author,
        AuthorName = "Alice",
        Body = body,
        BodyHtml = html,
        SentAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        Attachments = attachments
    };

    private static SupportAttachment Image(string id, string contentId, string contentType = "image/png", long size = 3, string name = "shot.png") => new()
    {
        Id = id,
        FileName = name,
        ContentType = contentType,
        Size = size,
        Inline = true,
        ContentId = contentId
    };

    [Fact]
    public void AFormattedEntry_IsShownAsItsFormatting()
    {
        var view = RenderTranscript(Entry("<p>Try <b>this</b>:</p><ul><li>Restart</li></ul>"));

        var body = view.Find("[data-body-html='2']");
        Assert.Equal("Try this:", body.QuerySelector("p")!.TextContent);
        Assert.Equal("this", body.QuerySelector("b")!.TextContent);
        Assert.Equal("Restart", body.QuerySelector("li")!.TextContent);
        Assert.DoesNotContain("Plain", view.Markup);
    }

    /// <summary>Whatever reached storage, and however, it is shown only as today's allow-list permits.</summary>
    [Fact]
    public void StoredFormatting_IsSanitizedAgainAtRender()
    {
        var view = RenderTranscript(Entry("<p onclick=\"steal()\">Hi<script>steal()</script></p><iframe src=\"https://evil.example\"></iframe><a href=\"javascript:steal()\">x</a>"));

        var body = view.Find("[data-body-html='2']");
        Assert.Empty(body.QuerySelectorAll("script, iframe, [onclick]"));
        Assert.Empty(body.QuerySelectorAll("a"));
        Assert.Contains("Hi", body.TextContent);
    }

    [Fact]
    public void APlainEntry_IsShownAsText_WithItsLineBreaks()
    {
        var view = RenderTranscript(Entry(null, "<b>not markup</b>\nsecond line"));

        Assert.Empty(view.FindAll("[data-body-html]"));
        Assert.Contains("&lt;b&gt;not markup&lt;/b&gt;", view.Markup);
    }

    [Fact]
    public void AnImageTheFormattingNames_IsShownInPlace_FromContentReadThroughTheService()
    {
        var image = Image("a1", "shot@example.com");
        OpensTo(image, [1, 2, 3]);

        var view = RenderTranscript(Entry("<p>Look: <img src=\"cid:shot@example.com\" alt=\"screenshot\"></p>", "Look:", image));

        view.WaitForAssertion(() => Assert.Equal("data:image/png;base64,AQID", view.Find("[data-body-html='2'] img").GetAttribute("src")));
        _cases.Verify(x => x.OpenAttachmentAsync(TeamKey, CaseId, 2, "a1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AnImageShownInPlace_IsNotListedAgain_ButOtherFilesAre()
    {
        var image = Image("a1", "shot@example.com");
        var report = new SupportAttachment { Id = "a2", FileName = "report.pdf", ContentType = "application/pdf", Size = 10 };
        OpensTo(image, [1, 2, 3]);

        var view = RenderTranscript(Entry("<p><b>Look</b> <img src=\"cid:shot@example.com\"></p>", "Look", image, report));

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-body-html='2'] img")));
        Assert.Equal(["a2"], view.FindAll("[data-attachment]").Select(x => x.GetAttribute("data-attachment")).ToArray());
    }

    [Fact]
    public void AnInlineImageTheFormattingDoesNotName_IsListed()
    {
        var image = Image("a1", "unused@example.com");

        var view = RenderTranscript(Entry("<p><b>No picture here</b></p>", "No picture here", image));

        Assert.Equal(["a1"], view.FindAll("[data-attachment]").Select(x => x.GetAttribute("data-attachment")).ToArray());
        _cases.Verify(x => x.OpenAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AnImageOverTheSizeCap_IsNamedInPlace_NeverRead_AndListed()
    {
        var image = Image("a1", "big@example.com", size: SupportTranscript.MaxInlineImageBytes + 1, name: "big.png");

        var view = RenderTranscript(Entry("<p><b>Big:</b> <img src=\"cid:big@example.com\"></p>", "Big:", image));

        Assert.Contains("big.png", view.Find("[data-body-html='2']").TextContent);
        Assert.Empty(view.FindAll("[data-body-html='2'] img"));
        Assert.Equal(["a1"], view.FindAll("[data-attachment]").Select(x => x.GetAttribute("data-attachment")).ToArray());
        _cases.Verify(x => x.OpenAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AnImageWhoseContentTurnsOutLargerThanDeclared_IsNamed_AndListed()
    {
        var image = Image("a1", "liar@example.com", name: "liar.png");
        OpensTo(image, new byte[SupportTranscript.MaxInlineImageBytes + 1]);

        var view = RenderTranscript(Entry("<p><b>Look</b> <img src=\"cid:liar@example.com\"></p>", "Look", image));

        view.WaitForAssertion(() => Assert.Equal(["a1"], view.FindAll("[data-attachment]").Select(x => x.GetAttribute("data-attachment")).ToArray()));
        Assert.Empty(view.FindAll("[data-body-html='2'] img"));
        Assert.Contains("liar.png", view.Find("[data-body-html='2']").TextContent);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("image/bmp")]
    public void AnImageOfATypeThatCouldRunScriptOrIsNotShown_IsNamed_AndListed(string contentType)
    {
        var image = Image("a1", "x@example.com", contentType, name: "drawing.svg");

        var view = RenderTranscript(Entry("<p><b>See</b> <img src=\"cid:x@example.com\"></p>", "See", image));

        Assert.Empty(view.FindAll("[data-body-html='2'] img"));
        Assert.Contains("drawing.svg", view.Find("[data-body-html='2']").TextContent);
        Assert.Single(view.FindAll("[data-attachment]"));
    }

    [Fact]
    public void AnImageTheServiceCannotOpen_IsNamed_AndListed()
    {
        var image = Image("a1", "gone@example.com", name: "gone.png");

        var view = RenderTranscript(Entry("<p><b>See</b> <img src=\"cid:gone@example.com\"></p>", "See", image));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-attachment]")));
        Assert.Contains("gone.png", view.Find("[data-body-html='2']").TextContent);
    }

    [Fact]
    public void WithoutACase_ImagesAreNamed_NeverRead_AndListed()
    {
        var image = Image("a1", "shot@example.com");

        var view = RenderTranscript(Entry("<p><b>Look</b> <img src=\"cid:shot@example.com\"></p>", "Look", image), caseId: null);

        Assert.Empty(view.FindAll("[data-body-html='2'] img"));
        Assert.Single(view.FindAll("[data-attachment]"));
        _cases.Verify(x => x.OpenAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AnImageIsReadOnce_HoweverOftenTheTranscriptIsRenderedAgain()
    {
        var image = Image("a1", "shot@example.com");
        OpensTo(image, [1, 2, 3]);
        var view = RenderTranscript(Entry("<p><b>Look</b> <img src=\"cid:shot@example.com\"></p>", "Look", image));
        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-body-html='2'] img")));

        view.Render(p => p.Add(x => x.Messages, [Entry("<p><b>Look</b> <img src=\"cid:shot@example.com\"></p>", "Look", image)]));

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-body-html='2'] img")));
        _cases.Verify(x => x.OpenAttachmentAsync(TeamKey, CaseId, 2, "a1", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Radzen's dark themes keep the base scale light while the text turns light, so a bubble on <c>--rz-base-*</c>
    /// puts light text on a light background.
    /// </summary>
    [Fact]
    public void NoBubble_TakesItsBackgroundFromTheLightBaseScale()
    {
        var view = RenderTranscript(
            Entry(null) with { Sequence = 1 },
            Entry(null) with { Sequence = 2, AuthorIdentity = "support-agent", AuthorName = "Support", AsSupport = true });

        var bubbles = view.FindAll("[data-bubble]");
        Assert.Equal(2, bubbles.Count);

        foreach (var bubble in bubbles)
        {
            var style = bubble.GetAttribute("style") ?? string.Empty;
            Assert.Contains("background", style);
            Assert.DoesNotMatch(@"--rz-base-(50|100|200|300)\b", style);
            Assert.DoesNotContain("#fff", style, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("white", style, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OpensTo(SupportAttachment attachment, byte[] content)
        => _cases.Setup(x => x.OpenAttachmentAsync(TeamKey, CaseId, 2, attachment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SupportAttachmentContent { Attachment = attachment, Content = new MemoryStream(content) });

    private IRenderedComponent<SupportTranscript> RenderTranscript(SupportMessage message, string caseId = CaseId) => RenderTranscript([message], caseId);

    private IRenderedComponent<SupportTranscript> RenderTranscript(params SupportMessage[] messages) => RenderTranscript(messages, CaseId);

    private IRenderedComponent<SupportTranscript> RenderTranscript(SupportMessage[] messages, string caseId)
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        Services.AddSingleton(_cases.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportTranscript>(p => p
            .Add(x => x.Messages, messages)
            .Add(x => x.CaseAuthorIdentity, Author)
            .Add(x => x.TeamKey, TeamKey)
            .Add(x => x.CaseId, caseId));
    }
}
