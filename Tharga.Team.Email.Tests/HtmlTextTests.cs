using Tharga.Team.Email;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// Flattening an HTML mail body into the text a transcript stores.
/// </summary>
/// <remarks>
/// The output is read by people and stored against a case, so what matters is that it is legible and bounded
/// — not that it reproduces the document.
/// </remarks>
public class HtmlTextTests
{
    [Fact]
    public void TagsAreRemoved_AndParagraphsBecomeBlankLines()
    {
        Assert.Equal("Any news?\n\nThanks", HtmlText.ToPlainText("<p>Any <b>news</b>?</p><p>Thanks</p>"));
    }

    [Fact]
    public void LineBreaks_BecomeNewlines()
    {
        Assert.Equal("One\nTwo", HtmlText.ToPlainText("One<br>Two"));
        Assert.Equal("One\nTwo", HtmlText.ToPlainText("One<br />Two"));
    }

    /// <summary>
    /// Style and script are markup, not message. Stripping only their tags would leave a wall of CSS in the
    /// transcript, which is the common way a flattened mail becomes unreadable.
    /// </summary>
    [Fact]
    public void StyleAndScriptContent_IsDroppedWhole()
    {
        var text = HtmlText.ToPlainText("<style>.a{color:red}</style><p>Hello</p><script>alert(1)</script>");

        Assert.Equal("Hello", text);
    }

    [Fact]
    public void Entities_AreDecoded()
    {
        Assert.Equal("Tom & Jerry \"quoted\"", HtmlText.ToPlainText("<p>Tom &amp; Jerry &quot;quoted&quot;</p>"));
    }

    [Fact]
    public void RunsOfWhitespace_AreCollapsed()
    {
        Assert.Equal("One Two", HtmlText.ToPlainText("<p>One     \t  Two</p>"));
    }

    [Fact]
    public void RunsOfBlankLines_AreCollapsedToOne()
    {
        Assert.Equal("One\n\nTwo", HtmlText.ToPlainText("<p>One</p><p></p><p></p><p>Two</p>"));
    }

    [Fact]
    public void ListItemsAndHeadings_EachEndALine()
    {
        Assert.Equal("Title\n\n- One\n- Two", HtmlText.ToPlainText("<h1>Title</h1><ul><li>One</li><li>Two</li></ul>"));
    }

    [Fact]
    public void ANumberedList_IsNumbered_FromItsStart()
    {
        Assert.Equal("1. One\n2. Two\n\n3. Three", HtmlText.ToPlainText("<ol><li>One</li><li>Two</li></ol><ol start=\"3\"><li>Three</li></ol>"));
    }

    [Fact]
    public void ANestedList_IsIndented()
    {
        Assert.Equal("- One\n  - One a\n  - One b\n- Two", HtmlText.ToPlainText("<ul><li>One<ul><li>One a</li><li>One b</li></ul></li><li>Two</li></ul>"));
    }

    [Fact]
    public void AListItemHoldingAParagraph_StaysOnItsMarkersLine()
    {
        Assert.Equal("Steps:\n\n- <p> inside", HtmlText.ToPlainText("<p>Steps:</p><ul><li><p>&lt;p&gt; inside</p></li></ul>"));
    }

    [Theory]
    [InlineData("<a href=\"https://example.com/help\">the help page</a>", "the help page (https://example.com/help)")]
    [InlineData("<a href=\"https://example.com/help\">https://example.com/help</a>", "https://example.com/help")]
    [InlineData("<a href=\"https://example.com/help\"></a>", "https://example.com/help")]
    [InlineData("<a href=\"mailto:help@example.com\">help@example.com</a>", "help@example.com")]
    [InlineData("<a href=\"mailto:help@example.com\">write to us</a>", "write to us (help@example.com)")]
    [InlineData("<a href=\"javascript:alert(1)\">Click</a>", "Click")]
    public void ALink_IsItsTextAndItsAddress(string html, string expected)
    {
        Assert.Equal($"See {expected}.", HtmlText.ToPlainText($"<p>See {html}.</p>"));
    }

    [Fact]
    public void LinesInDivs_AsAMailEditorWritesThem_StayLines()
    {
        Assert.Equal("Hi\n\nThanks\nA User", HtmlText.ToPlainText("Hi<div><br></div><div>Thanks</div><div>A User</div>"));
    }

    [Fact]
    public void AQuote_IsMarkedLineByLine()
    {
        Assert.Equal("Before\n\n> One\n> Two\n\nAfter", HtmlText.ToPlainText("<p>Before</p><blockquote>One<br>Two</blockquote><p>After</p>"));
    }

    [Fact]
    public void PreformattedText_KeepsItsSpacing()
    {
        Assert.Equal("Log:\n\nline  1\n    line 2", HtmlText.ToPlainText("<p>Log:</p><pre>line  1\n    line 2</pre>"));
    }

    [Fact]
    public void ATable_ReadsRowByRow()
    {
        Assert.Equal("Name Value\nA 1", HtmlText.ToPlainText("<table><tr><th>Name</th><th>Value</th></tr><tr><td>A</td><td>1</td></tr></table>"));
    }

    [Fact]
    public void HiddenText_IsLeftOut()
    {
        Assert.Equal("Hello", HtmlText.ToPlainText("<div style=\"display: none\">Preheader</div><p>Hello</p><span hidden>x</span>"));
    }

    [Fact]
    public void NonBreakingSpaces_ReadAsSpaces()
    {
        Assert.Equal("A B", HtmlText.ToPlainText("<p>A&nbsp;&nbsp;B&nbsp;</p>"));
    }

    [Fact]
    public void NothingAtAll_IsEmpty()
    {
        Assert.Equal(string.Empty, HtmlText.ToPlainText(null));
        Assert.Equal(string.Empty, HtmlText.ToPlainText(""));
        Assert.Equal(string.Empty, HtmlText.ToPlainText("   "));
        Assert.Equal(string.Empty, HtmlText.ToPlainText("<p></p>"));
    }

    /// <summary>
    /// Mail HTML is routinely malformed. The requirement is that it does not throw and does not return
    /// markup, not that it recovers the intended document.
    /// </summary>
    [Fact]
    public void MalformedMarkup_StillYieldsText()
    {
        Assert.Equal("Hello", HtmlText.ToPlainText("<p><b>Hello</p>"));
        Assert.Equal("Hello", HtmlText.ToPlainText("Hello<div"));
    }
}
