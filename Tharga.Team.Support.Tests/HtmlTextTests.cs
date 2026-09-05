using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Tests;

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
        Assert.Equal("Title\n\nOne\nTwo", HtmlText.ToPlainText("<h1>Title</h1><ul><li>One</li><li>Two</li></ul>"));
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
