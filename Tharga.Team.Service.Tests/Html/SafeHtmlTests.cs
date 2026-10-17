using Tharga.Team.Service.Html;

namespace Tharga.Team.Service.Tests.Html;

/// <summary>
/// The one sanitizer: formatting survives, anything that could run, fetch, hide or restyle does not.
/// </summary>
/// <remarks>
/// <b>Every assertion is on the output, never on how it was produced.</b> The library underneath may change how it
/// removes something; what may come out of it may not.
/// </remarks>
public class SafeHtmlTests
{
    [Theory]
    [InlineData("<p>Hi<script>alert(1)</script></p>")]
    [InlineData("<p>Hi</p><SCRIPT src=\"https://evil.example/x.js\"></SCRIPT>")]
    [InlineData("<p>Hi<style>p { color: red }</style></p>")]
    [InlineData("<p>Hi</p><iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<p>Hi</p><object data=\"x.swf\"></object><embed src=\"x.swf\">")]
    [InlineData("<p>Hi</p><form action=\"https://evil.example\"><input name=\"password\"><button>Send</button></form>")]
    [InlineData("<p>Hi</p><svg><script>alert(1)</script></svg>")]
    public void WhatCouldRunOrAskForInput_IsGoneWithItsContent(string html)
    {
        var clean = SafeHtml.Sanitize(html);

        Assert.Equal("<p>Hi</p>", clean);
    }

    [Theory]
    [InlineData("<p onclick=\"alert(1)\">Hi</p>")]
    [InlineData("<p onmouseover=\"alert(1)\" style=\"color: red; background: white\" class=\"x\" id=\"y\">Hi</p>")]
    [InlineData("<p data-x=\"1\" dir=\"ltr\">Hi</p>")]
    public void EventHandlersStylesAndOtherAttributes_AreRemoved(string html)
    {
        Assert.Equal("<p>Hi</p>", SafeHtml.Sanitize(html));
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">Click</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">Click</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">Click</a>")]
    [InlineData("<a href=\"vbscript:msgbox\">Click</a>")]
    [InlineData("<a href=\"/admin/delete\">Click</a>")]
    [InlineData("<a href=\"file:///c:/secret.txt\">Click</a>")]
    [InlineData("<a href=\"cid:part1\">Click</a>")]
    [InlineData("<a>Click</a>")]
    public void ALinkToAnythingButTheWebOrMail_KeepsItsTextAndLosesTheLink(string html)
    {
        Assert.Equal("Click", SafeHtml.Sanitize(html));
    }

    [Theory]
    [InlineData("https://example.com/help")]
    [InlineData("http://example.com/")]
    [InlineData("mailto:help@example.com")]
    public void AWebOrMailLink_IsKept_AndOpensApartWithNoOpenerOrReferrer(string href)
    {
        var clean = SafeHtml.Sanitize($"<a href=\"{href}\" target=\"_self\" rel=\"opener\">Help</a>");

        Assert.Equal($"<a href=\"{href}\" rel=\"noopener noreferrer\" target=\"_blank\">Help</a>", clean);
    }

    [Theory]
    [InlineData("<img src=\"https://tracker.example/pixel.gif\" alt=\"Logo\">")]
    [InlineData("<img src=\"http://tracker.example/pixel.gif\" alt=\"Logo\">")]
    [InlineData("<img src=\"//tracker.example/pixel.gif\" alt=\"Logo\">")]
    [InlineData("<img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"Logo\">")]
    [InlineData("<img src=\"javascript:alert(1)\" alt=\"Logo\">")]
    [InlineData("<img alt=\"Logo\">")]
    public void AnImageFromAnywhereButTheMailItself_BecomesItsAltText(string html)
    {
        Assert.Equal("<p>Logo</p>", SafeHtml.Sanitize($"<p>{html}</p>"));
    }

    [Fact]
    public void ARemoteImageWithoutAltText_LeavesNothing()
    {
        Assert.Equal("<p>Hi</p>", SafeHtml.Sanitize("<p>Hi<img src=\"https://tracker.example/pixel.gif\" width=\"1\" height=\"1\"></p>"));
    }

    [Fact]
    public void AnImageNamingAPartOfTheMail_IsKeptAsItsReference()
    {
        Assert.Equal("<p><img src=\"cid:logo@example.com\" alt=\"Logo\"></p>", SafeHtml.Sanitize("<p><img src=\"cid:logo@example.com\" alt=\"Logo\" onerror=\"alert(1)\" style=\"width:9000px\"></p>"));
    }

    [Theory]
    [InlineData("<p>Plain</p>")]
    [InlineData("<h1>One</h1><h2>Two</h2><h3>Three</h3><h4>Four</h4>")]
    [InlineData("<p><b>b</b><strong>strong</strong><i>i</i><em>em</em><u>u</u><s>s</s><strike>strike</strike><del>del</del></p>")]
    [InlineData("<ul><li>One</li><li>Two<ol start=\"3\"><li>Three</li></ol></li></ul>")]
    [InlineData("<blockquote>Quoted</blockquote><pre><code>var x = 1;</code></pre>")]
    [InlineData("<table><caption>Plan</caption><thead><tr><th colspan=\"2\">Head</th></tr></thead><tbody><tr><td rowspan=\"2\">A</td><td>B</td></tr></tbody></table>")]
    [InlineData("<div>Line one</div><div>Line two<br>and three</div>")]
    public void AllowedFormatting_IsKeptAsWritten(string html)
    {
        Assert.Equal(html, SafeHtml.Sanitize(html));
    }

    [Fact]
    public void ATagOutsideTheList_IsUnwrapped_AndItsTextKept()
    {
        Assert.Equal("<p>Hello world</p>", SafeHtml.Sanitize("<p><font color=\"red\"><span class=\"x\">Hello</span></font> <o:p>world</o:p></p>"));
    }

    [Fact]
    public void ABlockOutsideTheList_StillReadsAsItsOwnLine()
    {
        Assert.Equal("<div>One</div><div>Two</div>", SafeHtml.Sanitize("<section>One</section><h6>Two</h6>"));
    }

    [Theory]
    [InlineData("<p>Hi</p><div style=\"display:none\">Preheader text</div>")]
    [InlineData("<p>Hi</p><div style=\"DISPLAY: NONE;\">Preheader text</div>")]
    [InlineData("<p>Hi</p><span style=\"visibility:hidden\">Hidden</span>")]
    [InlineData("<p>Hi</p><div style=\"mso-hide:all\">Hidden</div>")]
    [InlineData("<p>Hi</p><div hidden>Hidden</div>")]
    public void TextTheSenderHid_StaysHidden_RatherThanAppearingOnceItsStyleIsGone(string html)
    {
        Assert.Equal("<p>Hi</p>", SafeHtml.Sanitize(html));
    }

    [Fact]
    public void AWholeMailDocument_GivesItsBodyAlone()
    {
        var clean = SafeHtml.Sanitize("<html><head><title>Mail</title><style>body{background:#fff}</style></head><body bgcolor=\"white\"><p>Hi</p></body></html>");

        Assert.Equal("<p>Hi</p>", clean);
    }

    [Fact]
    public void Comments_IncludingConditionalOnes_AreRemoved()
    {
        Assert.Equal("<p>Hi</p>", SafeHtml.Sanitize("<!--[if mso]><p>Outlook only</p><![endif]--><p>Hi</p><!-- note -->"));
    }

    [Fact]
    public void SanitizingTwice_ChangesNothingTheFirstTimeDidNot()
    {
        const string html = "<p onclick=\"x()\"><a href=\"https://example.com\">Link</a> <img src=\"cid:a@b\" alt=\"A\"> <b>bold</b></p><script>x()</script>";

        var once = SafeHtml.Sanitize(html);

        Assert.Equal(once, SafeHtml.Sanitize(once));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<p><br></p>")]
    [InlineData("<p>&nbsp;</p><div> </div>")]
    [InlineData("<p>\u200B</p>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<ul><li></li></ul>")]
    public void NothingReadable_IsEmpty(string html)
    {
        Assert.True(SafeHtml.IsEmpty(html));
        Assert.Null(SafeHtml.Sanitize(html));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("<p>x</p>")]
    [InlineData("<p><img src=\"cid:a@b\"></p>")]
    public void AnythingReadable_IsNotEmpty(string html)
    {
        Assert.False(SafeHtml.IsEmpty(html));
    }

    [Theory]
    [InlineData("Hello", false)]
    [InlineData("Hello<div>World</div><div><br></div>", false)]
    [InlineData("<p>One</p><p>Two<br>Three</p>", false)]
    [InlineData("<p><span style=\"color:red\">Not kept</span></p>", false)]
    [InlineData("<p><b>Bold</b></p>", true)]
    [InlineData("<ul><li>One</li></ul>", true)]
    [InlineData("<p><a href=\"https://example.com\">Link</a></p>", true)]
    [InlineData("<p><img src=\"cid:a@b\"></p>", true)]
    [InlineData("<p><img src=\"https://example.com/x.png\"></p>", false)]
    public void Formatting_IsAnythingBeyondParagraphsAndLineBreaks(string html, bool formatted)
    {
        Assert.Equal(formatted, SafeHtml.IsFormatted(html));
    }

    [Fact]
    public void TheContentIdsAnImageNames_AreListedOnce_InOrder()
    {
        var ids = SafeHtml.ContentIds("<img src=\"cid:b@x\"><p><img src=\"CID:a@x\"><img src=\"cid:b@x\"><img src=\"https://example.com/c.png\"><img src=\"cid:%3Cc%40x%3E\"></p>");

        Assert.Equal(["b@x", "a@x", "c@x"], ids);
    }

    [Fact]
    public void AnImage_CanBeShownFromItsContent()
    {
        var clean = SafeHtml.Sanitize("<p><img src=\"cid:logo@x\" alt=\"Logo\"></p>", id => id == "logo@x" ? HtmlImage.FromContent("image/png", [1, 2, 3]) : null);

        Assert.Equal("<p><img src=\"data:image/png;base64,AQID\" alt=\"Logo\"></p>", clean);
    }

    [Fact]
    public void AnImageNotShown_IsDescribedByItsAltText_OrByTheTextGiven()
    {
        var withAlt = SafeHtml.Sanitize("<p><img src=\"cid:a@x\" alt=\"Chart\"></p>", _ => HtmlImage.Described("chart.png"));
        var withoutAlt = SafeHtml.Sanitize("<p><img src=\"cid:a@x\"></p>", _ => HtmlImage.Described("chart.png"));
        var unknown = SafeHtml.Sanitize("<p>Hi<img src=\"cid:a@x\"></p>", _ => null);

        Assert.Equal("<p>Chart</p>", withAlt);
        Assert.Equal("<p>chart.png</p>", withoutAlt);
        Assert.Equal("<p>Hi</p>", unknown);
    }

    [Fact]
    public void AReferenceCanBeKept_ForAMailCarryingTheImage()
    {
        Assert.Equal("<p><img src=\"cid:a@x\"></p>", SafeHtml.Sanitize("<p><img src=\"cid:a@x\"></p>", _ => HtmlImage.Keep));
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("image/bmp")]
    [InlineData(null)]
    public void OnlyAnImageTypeShownWithoutScript_CanBeShownFromContent(string contentType)
    {
        Assert.Null(HtmlImage.FromContent(contentType, [1, 2, 3]));
    }

    [Fact]
    public void AnImageTypeWithParameters_IsShownAsItsMediaTypeAlone()
    {
        Assert.Equal("data:image/jpeg;base64,AQ==", HtmlImage.FromContent("IMAGE/JPEG; name=photo.jpg", [1]).Source);
    }

    [Fact]
    public void PlainText_IsEscapedWithItsLineBreaks()
    {
        Assert.Equal("&lt;b&gt;Tom &amp; Jerry&lt;/b&gt;<br>line two<br><br>after a blank", SafeHtml.FromPlainText("<b>Tom & Jerry</b>\r\nline two\n\nafter a blank"));
    }
}
