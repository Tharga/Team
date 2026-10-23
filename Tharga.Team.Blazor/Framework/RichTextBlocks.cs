namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// The block styles the editor offers: a paragraph, a heading and a subheading. Pure, so the mapping between what the
/// browser reports and what the toolbar shows is testable without a browser.
/// </summary>
/// <remarks>
/// <b>Why not Radzen's own style picker.</b> It lists every heading from h1 to h6, while <c>SafeHtml</c> keeps h1 to h4
/// and turns h5 and h6 into plain lines, so an author could pick a style that comes back as a paragraph once saved. These
/// two are the ones a document needs and the reading side shows.
/// </remarks>
public static class RichTextBlocks
{
    /// <summary>The paragraph element.</summary>
    public const string Paragraph = "p";

    /// <summary>The heading element.</summary>
    public const string Heading = "h2";

    /// <summary>The subheading element.</summary>
    public const string Subheading = "h3";

    /// <summary>The elements the toolbar offers, in the order it lists them.</summary>
    public static IReadOnlyList<string> Offered { get; } = [Paragraph, Heading, Subheading];

    /// <summary>
    /// The style to show as selected for the block the browser reports at the caret, or null when it is a style the
    /// toolbar does not offer, so the picker shows its label instead of naming the wrong style.
    /// </summary>
    /// <remarks>
    /// Browsers disagree on the form: most report <c>h2</c>, some report <c>Heading 2</c>. A block that is no heading at
    /// all, such as the <c>div</c> a line of typed text starts as, counts as a paragraph.
    /// </remarks>
    public static string Selected(string reported)
    {
        var block = (reported ?? string.Empty).Trim().ToLowerInvariant();

        return block switch
        {
            "" or "p" or "div" or "paragraph" or "normal" => Paragraph,
            "h2" or "heading 2" => Heading,
            "h3" or "heading 3" => Subheading,
            _ => null,
        };
    }

    /// <summary>The value the browser's block-format command takes for <paramref name="block"/>.</summary>
    /// <remarks>Brackets included: that form works in every browser, where the bare name does not in all of them.</remarks>
    public static string Command(string block) => $"<{block}>";
}
