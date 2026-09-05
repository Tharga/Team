using System.Net;
using System.Text.RegularExpressions;

namespace Tharga.Team.Support.Email;

/// <summary>
/// Flattens an HTML mail body to the plain text a transcript stores.
/// </summary>
/// <remarks>
/// <b>Written here rather than taken from a library</b> for one reason: the output is stored in a case and
/// read by people, so it needs to be stable and bounded. A general-purpose converter optimises for
/// reproducing a document; this optimises for a legible paragraph that does not change shape when the
/// library updates.
/// <para>
/// It is deliberately not an HTML parser and does not try to be. Mail HTML is frequently malformed, and the
/// failure that matters is not "this table rendered badly" — it is a body that turns out to be unreadable or
/// enormous.
/// </para>
/// </remarks>
internal static partial class HtmlText
{
    public static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var text = InvisibleElements().Replace(html, " ");

        text = BlockElements().Replace(text, "\n\n");
        text = LineBreakElements().Replace(text, "\n");
        text = Tags().Replace(text, string.Empty);
        text = DanglingTag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        text = HorizontalWhitespace().Replace(text, " ");
        text = BlankLines().Replace(text, "\n\n");

        return text.Trim();
    }

    /// <summary>Content that is markup rather than message — dropped whole, not stripped of its tags.</summary>
    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InvisibleElements();

    /// <summary>Ends a block, so the next text starts a paragraph.</summary>
    [GeneratedRegex(@"<\s*/\s*(p|div|h[1-6]|table|blockquote)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockElements();

    /// <summary>Ends a line without ending a block — list items and table rows read as a list, not prose.</summary>
    [GeneratedRegex(@"<\s*(br|/\s*li|/\s*tr)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakElements();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tags();

    /// <summary>
    /// An unterminated tag at the end of a truncated body. Left alone it is markup in the transcript, which
    /// is the failure this converter exists to avoid.
    /// </summary>
    [GeneratedRegex("<[^>]*$", RegexOptions.Singleline)]
    private static partial Regex DanglingTag();

    /// <summary>Spaces and tabs only — newlines are the structure that survived the tags.</summary>
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@"\n\s*\n\s*(\n\s*)+")]
    private static partial Regex BlankLines();
}
