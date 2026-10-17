using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Tharga.Team.Email;

/// <summary>
/// Cuts the quoted thread and signature off an HTML reply, leaving what the person actually wrote.
/// </summary>
/// <remarks>
/// <b>The HTML counterpart of <see cref="QuotedText"/>, and conservative for the same reason.</b> It cuts only at
/// the containers mail clients put a quote or a signature in — Gmail's, Outlook's, Apple Mail's and
/// Thunderbird's, and Yahoo's — and when it finds none it says so, so a caller can tell an HTML body that still
/// carries its quote in some shape not recognised here from one that never had one.
/// <para>
/// <b>It works on the mail's own markup, before sanitizing</b>, because the classes and ids that mark a quote are
/// exactly what sanitizing removes. The result is to be sanitized before it is kept or shown.
/// </para>
/// </remarks>
internal static class QuotedHtml
{
    private const string Markers =
        ".gmail_quote, .gmail_quote_container, .gmail_signature_prefix, .gmail_signature, " +
        "#divRplyFwdMsg, #appendonsend, #mail-editor-reference-message-container, " +
        "blockquote[type=cite], .moz-cite-prefix, .moz-signature, .yahoo_quoted";

    /// <summary>Outlook's own header block above a quote, drawn as a thin rule in one of two colours.</summary>
    private static readonly string[] OutlookRules = ["border-top:solid#e1e1e1", "border-top:solid#b5c4df"];

    /// <summary>The body's markup up to where its quote or signature begins, and whether one was found.</summary>
    public static (string Html, bool Cut) Trim(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return (html, false);

        var body = new HtmlParser().ParseDocument(html).Body;
        if (body == null) return (html, false);

        var marker = body.QuerySelectorAll("*").FirstOrDefault(IsMarker);
        if (marker == null) return (html, false);

        CutFrom(marker, body);
        TrimEnd(body);

        return (body.InnerHtml, true);
    }

    private static bool IsMarker(IElement element)
    {
        if (element.Matches(Markers)) return true;
        if (element.LocalName != "div" || !element.HasAttribute("style")) return false;

        var style = new string(element.GetAttribute("style").Where(x => !char.IsWhiteSpace(x)).ToArray()).ToLowerInvariant();

        return OutlookRules.Any(style.Contains);
    }

    /// <summary>Removes the marker, everything after it, and everything after each of its ancestors.</summary>
    private static void CutFrom(INode marker, INode body)
    {
        var node = marker;

        while (node != null && node != body)
        {
            var parent = node.Parent;

            while (node.NextSibling != null) parent.RemoveChild(node.NextSibling);
            if (node == marker) parent.RemoveChild(node);

            node = parent;
        }
    }

    /// <summary>Removes what trails the reply once its quote is gone: blank lines, rules, and the line naming who wrote the quote.</summary>
    private static void TrimEnd(INode node)
    {
        while (node.LastChild is { } last)
        {
            switch (last)
            {
                case IText text when string.IsNullOrWhiteSpace(text.Data) || QuotedText.IsAttribution(text.Data):
                case IComment:
                    node.RemoveChild(last);
                    continue;
                case IElement element:
                    if (element.LocalName is "br" or "hr" || IsBlank(element) || QuotedText.IsAttribution(element.TextContent))
                    {
                        node.RemoveChild(last);
                        continue;
                    }

                    TrimEnd(element);

                    if (IsBlank(element))
                    {
                        node.RemoveChild(last);
                        continue;
                    }

                    return;
                default:
                    return;
            }
        }
    }

    private static bool IsBlank(IElement element)
        => element.LocalName != "img" && string.IsNullOrWhiteSpace(element.TextContent.Replace(' ', ' ')) && element.QuerySelector("img") == null;
}
