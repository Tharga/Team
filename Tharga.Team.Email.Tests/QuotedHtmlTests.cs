using Tharga.Team.Email;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// The quoted thread and signature are cut off an HTML reply at the containers mail clients put them in, and a body
/// with none of those is reported as uncut rather than guessed at.
/// </summary>
public class QuotedHtmlTests
{
    [Fact]
    public void AGmailReply_IsCutAtItsQuote_WithTheAttribution()
    {
        const string html = "<div dir=\"ltr\">Fixed, <b>thanks</b>.</div><br><div class=\"gmail_quote\"><div dir=\"ltr\" class=\"gmail_attr\">On Fri, 17 Oct 2026 at 09:00, Support &lt;support@example.org&gt; wrote:<br></div><blockquote class=\"gmail_quote\">Try again.</blockquote></div>";

        var (trimmed, cut) = QuotedHtml.Trim(html);

        Assert.True(cut);
        Assert.Equal("<div dir=\"ltr\">Fixed, <b>thanks</b>.</div>", trimmed);
    }

    [Fact]
    public void AGmailSignature_IsCut()
    {
        var (trimmed, cut) = QuotedHtml.Trim("<div dir=\"ltr\">Thanks</div><span class=\"gmail_signature_prefix\">-- </span><br><div class=\"gmail_signature\">A User</div>");

        Assert.True(cut);
        Assert.Equal("<div dir=\"ltr\">Thanks</div>", trimmed);
    }

    [Fact]
    public void AnOutlookReply_IsCutAtItsHeaderBlock_WithTheRuleAboveIt()
    {
        const string html = "<div><p>It works now.</p></div><div id=\"appendonsend\"></div><hr style=\"display:inline-block;width:98%\"><div id=\"divRplyFwdMsg\"><b>From:</b> Support<br><b>Sent:</b> Friday</div><div>Try again.</div>";

        var (trimmed, cut) = QuotedHtml.Trim(html);

        Assert.True(cut);
        Assert.Equal("<div><p>It works now.</p></div>", trimmed);
    }

    [Fact]
    public void AnOutlookDesktopReply_IsCutAtItsDrawnRule()
    {
        const string html = "<div class=\"WordSection1\"><p class=\"MsoNormal\">Done.</p><p class=\"MsoNormal\">&nbsp;</p><div style=\"border:none;border-top:solid #E1E1E1 1.0pt;padding:3.0pt 0cm 0cm 0cm\"><p class=\"MsoNormal\"><b>From:</b> Support</p></div><p class=\"MsoNormal\">Try again.</p></div>";

        var (trimmed, cut) = QuotedHtml.Trim(html);

        Assert.True(cut);
        Assert.Equal("<div class=\"WordSection1\"><p class=\"MsoNormal\">Done.</p></div>", trimmed);
    }

    [Fact]
    public void AnAppleMailReply_IsCutAtItsCitation_AndTheLineNamingWhoWroteIt()
    {
        const string html = "<div>Works.</div><div><br><div>On 17 Oct 2026, at 09:00, Support\n wrote:</div><br><blockquote type=\"cite\"><div>Try again.</div></blockquote></div>";

        var (trimmed, cut) = QuotedHtml.Trim(html);

        Assert.True(cut);
        Assert.Equal("<div>Works.</div>", trimmed);
    }

    [Fact]
    public void AThunderbirdReply_IsCutAtItsCitePrefix()
    {
        var (trimmed, cut) = QuotedHtml.Trim("<p>Works.</p><div class=\"moz-cite-prefix\">On 17/10/2026 09:00, Support wrote:</div><blockquote type=\"cite\">Try again.</blockquote>");

        Assert.True(cut);
        Assert.Equal("<p>Works.</p>", trimmed);
    }

    [Fact]
    public void ABodyWithNoKnownQuote_IsLeftWhole_AndSaysSo()
    {
        const string html = "<p>Hello</p><blockquote>A quote I chose to make.</blockquote>";

        var (trimmed, cut) = QuotedHtml.Trim(html);

        Assert.False(cut);
        Assert.Equal(html, trimmed);
    }

    [Fact]
    public void AnImageBeforeTheQuote_IsKept()
    {
        var (trimmed, _) = QuotedHtml.Trim("<p>Screenshot:</p><p><img src=\"cid:shot@x\"></p><div class=\"gmail_quote\">old</div>");

        Assert.Equal("<p>Screenshot:</p><p><img src=\"cid:shot@x\"></p>", trimmed);
    }
}
