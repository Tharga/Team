using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// How an answer quotes the conversation so far: as Gmail does, one block with a bar at its left and older entries nested
/// inside it, or one block per entry.
/// </summary>
public class SupportQuoteStyleTests
{
    private const string Bar = "margin:0 0 0 0.8ex;border-left:1px solid #cccccc;padding-left:1ex";
    private static readonly DateTime First = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ByDefault_TheQuoteIsNested_AsGmailDoesIt()
    {
        var options = new SupportAnswerOptions();

        Assert.Equal(SupportQuoteStyle.Nested, options.QuoteStyle);
        Assert.Equal(SupportQuoteStyle.Nested, SupportAnswerFrame.Default.QuoteStyle);
    }

    [Fact]
    public void OneEarlierEntry_IsOneAttributionAndOneBarredBlock_InTextAndHtml()
    {
        var earlier = new[] { Entry(1, "Help me.") };

        Assert.Equal("Try this.\n\nOn 2026-10-17 08:00 UTC, A User wrote:\n> Help me.", EmailSupportChannel.Body(Answer(), earlier));
        Assert.Equal($"<p>Try <b>this</b>.</p><div class=\"gmail_quote\"><div class=\"gmail_attr\">On 2026-10-17 08:00 UTC, A User wrote:</div><blockquote class=\"gmail_quote\" style=\"{Bar}\">Help me.</blockquote></div>", EmailSupportChannel.Html(Answer(), earlier));
    }

    [Fact]
    public void SeveralEarlierEntries_AreOneBarredBlock_WithTheOlderOnesInsideIt()
    {
        var earlier = new[] { Entry(3, "Third."), Entry(2, "Second."), Entry(1, "First.") };

        Assert.Equal(
            "Try this.\n\n" +
            "On 2026-10-17 08:02 UTC, A User wrote:\n" +
            "> Third.\n" +
            ">\n" +
            "> On 2026-10-17 08:01 UTC, A User wrote:\n" +
            "> > Second.\n" +
            "> >\n" +
            "> > On 2026-10-17 08:00 UTC, A User wrote:\n" +
            "> > > First.",
            EmailSupportChannel.Body(Answer(), earlier));

        var html = EmailSupportChannel.Html(Answer(), earlier);

        Assert.Equal(1, Count(html, "<div class=\"gmail_quote\"><div class=\"gmail_attr\">On 2026-10-17 08:02") );
        Assert.Equal(3, Count(html, "<blockquote"));
        Assert.True(html.IndexOf("Third.", StringComparison.Ordinal) < html.IndexOf("Second.", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Second.", StringComparison.Ordinal) < html.IndexOf("First.", StringComparison.Ordinal));
        Assert.EndsWith("</blockquote></div></blockquote></div></blockquote></div>", html);
    }

    [Fact]
    public async Task PerEntry_KeepsOneBlockForEachEntry()
    {
        var frame = await SupportAnswerFrame.CreateAsync(new SupportAnswerOptions { QuoteStyle = SupportQuoteStyle.PerEntry }, new Texts());
        var earlier = new[] { Entry(2, "Second."), Entry(1, "First.") };

        Assert.Equal("Try this.\n\nOn 2026-10-17 08:01 UTC, A User wrote:\n> Second.\n\nOn 2026-10-17 08:00 UTC, A User wrote:\n> First.", EmailSupportChannel.Body(Answer(), earlier, frame));
        Assert.Equal("<p>Try <b>this</b>.</p><p>On 2026-10-17 08:01 UTC, A User wrote:</p><blockquote>Second.</blockquote><p>On 2026-10-17 08:00 UTC, A User wrote:</p><blockquote>First.</blockquote>", EmailSupportChannel.Html(Answer(), earlier, frame));
    }

    [Fact]
    public void NoEarlierEntries_QuotesNothing()
    {
        Assert.Equal("Try this.", EmailSupportChannel.Body(Answer(), []));
        Assert.Equal("<p>Try <b>this</b>.</p>", EmailSupportChannel.Html(Answer(), []));
    }

    [Fact]
    public void AnEarlierEntrysMarkup_IsStillSanitized_InsideTheNestedQuote()
    {
        var html = EmailSupportChannel.Html(Answer(), [Entry(2, "Two", "<p onclick=\"x()\">Two<script>x()</script></p>"), Entry(1, "One")]);

        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("script", html);
    }

    /// <summary>A customer's reply that carries our quote back must be cut where the quote starts, or the whole history comes into the case again.</summary>
    [Fact]
    public void ACustomersReply_CarryingOurQuote_IsCutAtIt_InHtmlAndText()
    {
        var earlier = new[] { Entry(2, "Second."), Entry(1, "First.") };

        var (html, htmlCut) = QuotedHtml.Trim("<p>Thanks, that works.</p>" + EmailSupportChannel.Html(Answer(), earlier).Substring("<p>Try <b>this</b>.</p>".Length));
        var text = QuotedText.Trim("Thanks, that works.\n\n" + EmailSupportChannel.Body(Answer(), earlier).Substring("Try this.\n\n".Length), out var textCut);

        Assert.True(htmlCut);
        Assert.Equal("<p>Thanks, that works.</p>", html);
        Assert.True(textCut);
        Assert.Equal("Thanks, that works.", text);
    }

    private static int Count(string text, string find)
    {
        var count = 0;
        for (var i = text.IndexOf(find, StringComparison.Ordinal); i >= 0; i = text.IndexOf(find, i + find.Length, StringComparison.Ordinal)) count++;

        return count;
    }

    private static SupportMessage Entry(int sequence, string body, string html = null) => new()
    {
        Sequence = sequence,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Body = body,
        BodyHtml = html,
        SentAt = First.AddMinutes(sequence - 1),
        Source = SupportChannelType.Email
    };

    private static SupportMessage Answer() => new()
    {
        Sequence = 10,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-2",
        AuthorName = "Support Agent",
        Body = "Try this.",
        BodyHtml = "<p>Try <b>this</b>.</p>",
        SentAt = First.AddMinutes(30),
        AsSupport = true
    };

    private sealed class Texts : ITextResolver
    {
        public Task<string> GetAsync(string key, string englishDefault) => Task.FromResult(englishDefault);
    }
}
