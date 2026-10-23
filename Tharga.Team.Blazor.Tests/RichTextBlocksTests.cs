using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Which block style the editor's picker shows for what the browser reports at the caret (Tharga/Team#345).
/// </summary>
public class RichTextBlocksTests
{
    [Theory]
    [InlineData("h2", RichTextBlocks.Heading)]
    [InlineData("H2", RichTextBlocks.Heading)]
    [InlineData("Heading 2", RichTextBlocks.Heading)]
    [InlineData("h3", RichTextBlocks.Subheading)]
    [InlineData("heading 3", RichTextBlocks.Subheading)]
    [InlineData("p", RichTextBlocks.Paragraph)]
    [InlineData("div", RichTextBlocks.Paragraph)]
    [InlineData("Normal", RichTextBlocks.Paragraph)]
    [InlineData("", RichTextBlocks.Paragraph)]
    [InlineData(null, RichTextBlocks.Paragraph)]
    [InlineData("  h2  ", RichTextBlocks.Heading)]
    public void TheReportedBlock_MapsToTheStyleShown(string reported, string expected)
    {
        Assert.Equal(expected, RichTextBlocks.Selected(reported));
    }

    [Theory]
    [InlineData("h1")]
    [InlineData("h4")]
    [InlineData("blockquote")]
    [InlineData("pre")]
    public void ABlockTheToolbarDoesNotOffer_ShowsNoStyleRatherThanTheWrongOne(string reported)
    {
        Assert.Null(RichTextBlocks.Selected(reported));
    }

    [Theory]
    [InlineData("h2", "<h2>")]
    [InlineData("p", "<p>")]
    public void TheCommand_IsTheBlockInBrackets(string block, string expected)
    {
        Assert.Equal(expected, RichTextBlocks.Command(block));
    }

    [Fact]
    public void EveryOfferedBlock_IsOneTheSanitizerKeepsAsItIs()
    {
        foreach (var block in RichTextBlocks.Offered)
        {
            var html = $"<{block}>Text</{block}>";

            Assert.Equal(html, Tharga.Team.Service.Html.SafeHtml.Sanitize(html));
        }
    }
}
