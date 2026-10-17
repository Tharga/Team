using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Tharga.Team.Email;

/// <summary>The plain-text rendering of HTML that a transcript, a notification and a mail's text alternative carry: paragraphs and lines kept, list items as <c>- </c> lines, quotes as <c>&gt; </c> lines, links as <c>text (address)</c>.</summary>
public static partial class HtmlText
{
    /// <summary>The plain text of HTML, or an empty string when it holds none.</summary>
    public static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var body = new HtmlParser().ParseDocument(html).Body;
        if (body == null) return string.Empty;

        var writer = new Writer();
        writer.WriteChildren(body);

        return writer.ToString();
    }

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "meta", "link", "template", "noscript", "iframe", "object", "embed",
        "svg", "math", "select", "option", "button", "input", "textarea", "img", "audio", "video", "canvas"
    };

    private static readonly HashSet<string> Paragraphs = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "table", "hr", "dl", "figure", "address"
    };

    private static readonly HashSet<string> Lines = new(StringComparer.OrdinalIgnoreCase)
    {
        "div", "tr", "dt", "dd", "section", "article", "header", "footer", "main", "nav", "aside", "caption",
        "figcaption", "details", "summary", "fieldset", "legend", "center", "form"
    };

    private static readonly HashSet<string> LinkSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    private const string MailtoScheme = "mailto:";

    private sealed class Writer
    {
        private readonly StringBuilder _text = new();
        private int _pendingBreaks;
        private bool _spacePending;
        private bool _afterMarker;
        private int _listDepth;

        public override string ToString()
        {
            var lines = _text.ToString().Split('\n').Select(x => x.TrimEnd());

            return BlankLines().Replace(string.Join('\n', lines), "\n\n").Trim('\n', ' ');
        }

        public void WriteChildren(INode node)
        {
            foreach (var child in node.ChildNodes) Write(child);
        }

        private void Write(INode node)
        {
            switch (node)
            {
                case IText text:
                    WriteText(text.Data);
                    break;
                case IElement element:
                    WriteElement(element);
                    break;
            }
        }

        private void WriteElement(IElement element)
        {
            var name = element.LocalName;

            if (Skipped.Contains(name) || IsHidden(element)) return;

            switch (name)
            {
                case "br":
                    Flush();
                    _text.Append('\n');
                    _spacePending = false;
                    _afterMarker = false;
                    return;
                case "ul" or "ol":
                    WriteList(element, name == "ol");
                    return;
                case "li":
                    WriteListItem(element, "- ");
                    return;
                case "pre":
                    Break(2);
                    Flush();
                    _text.Append(element.TextContent.Replace("\r\n", "\n").TrimEnd());
                    _afterMarker = false;
                    Break(2);
                    return;
                case "blockquote":
                    WriteQuote(element);
                    return;
                case "a":
                    WriteLink(element);
                    return;
                case "td" or "th":
                    _spacePending |= _text.Length > 0 && _text[^1] != '\n';
                    WriteChildren(element);
                    _spacePending = true;
                    return;
            }

            var breaks = Paragraphs.Contains(name) ? 2 : Lines.Contains(name) ? 1 : 0;

            Break(breaks);
            WriteChildren(element);
            Break(breaks);
        }

        private void WriteList(IElement list, bool numbered)
        {
            Break(_listDepth == 0 ? 2 : 1);
            _listDepth++;

            var number = int.TryParse(list.GetAttribute("start"), out var start) ? start : 1;

            foreach (var child in list.ChildNodes)
            {
                if (child is IElement { LocalName: "li" } item) WriteListItem(item, numbered ? $"{number++}. " : "- ");
                else Write(child);
            }

            _listDepth--;
            Break(_listDepth == 0 ? 2 : 1);
        }

        private void WriteListItem(IElement item, string marker)
        {
            _afterMarker = false;
            Break(1);
            Flush();
            _text.Append(new string(' ', Math.Max(0, _listDepth - 1) * 2)).Append(marker);
            _afterMarker = true;
            _spacePending = false;
            WriteChildren(item);
            _afterMarker = false;
            Break(1);
        }

        private void WriteQuote(IElement quote)
        {
            var inner = new Writer();
            inner.WriteChildren(quote);
            var quoted = inner.ToString();
            if (quoted.Length == 0) return;

            Break(2);
            Flush();
            _text.Append(string.Join('\n', quoted.Split('\n').Select(x => x.Length == 0 ? ">" : "> " + x)));
            _afterMarker = false;
            Break(2);
        }

        private void WriteLink(IElement link)
        {
            var href = link.GetAttribute("href")?.Trim();
            var start = _text.Length;

            WriteChildren(link);

            if (!IsLink(href)) return;

            var text = _text.ToString(start, _text.Length - start).Trim();
            var address = href.StartsWith(MailtoScheme, StringComparison.OrdinalIgnoreCase) ? href[MailtoScheme.Length..] : href;

            if (text.Length == 0) WriteText(address);
            else if (!string.Equals(text, address, StringComparison.OrdinalIgnoreCase) && !string.Equals(text, href, StringComparison.OrdinalIgnoreCase)) WriteText($" ({address})");
        }

        private void WriteText(string data)
        {
            var text = Whitespace().Replace(data.Replace(' ', ' '), " ");
            if (text.Length == 0) return;

            if (text[0] == ' ')
            {
                _spacePending = true;
                text = text[1..];
            }

            if (text.Length == 0) return;

            var endsWithSpace = text[^1] == ' ';
            if (endsWithSpace) text = text[..^1];

            Flush();

            if (_spacePending && _text.Length > 0 && _text[^1] != '\n' && !_afterMarker) _text.Append(' ');

            _text.Append(text);
            _afterMarker = false;
            _spacePending = endsWithSpace;
        }

        /// <summary>Asks for at least this many line breaks before whatever is written next; none right after a list marker.</summary>
        private void Break(int breaks)
        {
            if (breaks == 0 || _afterMarker) return;

            _pendingBreaks = Math.Max(_pendingBreaks, breaks);
            _spacePending = false;
        }

        private void Flush()
        {
            if (_pendingBreaks == 0) return;

            if (_text.Length > 0)
            {
                var trailing = 0;
                for (var i = _text.Length - 1; i >= 0 && _text[i] == '\n'; i--) trailing++;

                for (var i = trailing; i < _pendingBreaks; i++) _text.Append('\n');
            }

            _pendingBreaks = 0;
            _spacePending = false;
        }

        private static bool IsHidden(IElement element)
        {
            if (element.HasAttribute("hidden")) return true;

            var style = new string((element.GetAttribute("style") ?? string.Empty).Where(x => !char.IsWhiteSpace(x)).ToArray()).ToLowerInvariant();

            return style.Contains("display:none") || style.Contains("visibility:hidden") || style.Contains("mso-hide:all");
        }

        private static bool IsLink(string href)
            => Uri.TryCreate(href, UriKind.Absolute, out var uri) && LinkSchemes.Contains(uri.Scheme);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
