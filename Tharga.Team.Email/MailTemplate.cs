using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email;

/// <summary>Frames a mail's HTML in the host's template; the one renderer behind every HTML mail the toolkit sends and every preview of one.</summary>
/// <remarks>
/// <b>Placeholders are filled in one pass</b>, so a placeholder written inside a mail's own content is left as text rather
/// than filled. Without a template a mail keeps the HTML it had, with the footer after a rule, and a mail with text alone
/// stays text alone.
/// </remarks>
internal sealed partial class MailTemplate
{
    private const int PreheaderLength = 140;

    private readonly string _html;
    private readonly bool _hasFooter;

    public MailTemplate(IOptions<MailTemplateOptions> options)
    {
        _html = string.IsNullOrWhiteSpace(options?.Value?.Html) ? null : options.Value.Html;
        if (_html == null) return;

        RequireContent(_html);
        _hasFooter = Placeholder().Matches(_html).Any(x => IsNamed(x, MailTemplateOptions.FooterPlaceholder));
    }

    /// <summary>No template: each mail's own HTML, unframed.</summary>
    public static MailTemplate None { get; } = new(Options.Create(new MailTemplateOptions()));

    /// <summary>Whether a host template is set.</summary>
    public bool IsSet => _html != null;

    /// <summary>The HTML body a mail goes out with: <paramref name="html"/>, or the text as paragraphs, in the template with the subject, footer and preheader filled; without a template the HTML with the footer after a rule, or null when the mail has no HTML.</summary>
    /// <param name="subject">The subject line, as the recipient would see it in production.</param>
    /// <param name="text">The mail's text, before any footer.</param>
    /// <param name="html">The mail's HTML, already sanitized, or null when it has only text.</param>
    /// <param name="footerHtml">The footer's HTML, or null for none.</param>
    public string Render(string subject, string text, string html, string footerHtml = null)
    {
        if (_html == null) return html == null ? null : footerHtml == null ? html : $"{html}<hr>{footerHtml}";

        var content = html ?? FromText(text);
        if (!_hasFooter && footerHtml != null) content = $"{content}<hr>{footerHtml}";

        return Placeholder().Replace(_html, match =>
        {
            if (IsNamed(match, MailTemplateOptions.ContentPlaceholder)) return content;
            if (IsNamed(match, MailTemplateOptions.SubjectPlaceholder)) return WebUtility.HtmlEncode(subject ?? string.Empty);
            if (IsNamed(match, MailTemplateOptions.FooterPlaceholder)) return footerHtml ?? string.Empty;
            if (IsNamed(match, MailTemplateOptions.PreheaderPlaceholder)) return WebUtility.HtmlEncode(Preheader(text));
            return match.Value;
        });
    }

    /// <summary>Text as HTML: escaped, a paragraph per block separated by a blank line, a line break per line, and web addresses as links.</summary>
    internal static string FromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var blocks = BlankLine().Split(text.Replace("\r\n", "\n").Trim());
        var html = new StringBuilder();

        foreach (var block in blocks.Where(x => x.Trim().Length > 0))
        {
            html.Append("<p>").Append(string.Join("<br>", block.Split('\n').Select(Linked))).Append("</p>");
        }

        return html.ToString();
    }

    /// <summary>The start of the text on one line, cut at a word near <see cref="PreheaderLength"/> characters.</summary>
    internal static string Preheader(string text)
    {
        var line = string.Join(' ', (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= PreheaderLength) return line;

        var cut = line.LastIndexOf(' ', PreheaderLength);
        return $"{line[..(cut > 0 ? cut : PreheaderLength)]}…";
    }

    /// <exception cref="InvalidOperationException">The template has no <see cref="MailTemplateOptions.ContentPlaceholder"/>.</exception>
    internal static void RequireContent(string html)
    {
        if (!Placeholder().Matches(html ?? string.Empty).Any(x => IsNamed(x, MailTemplateOptions.ContentPlaceholder)))
            throw new InvalidOperationException($"The mail template has no {MailTemplateOptions.ContentPlaceholder} placeholder, so no mail would carry its own content.");
    }

    private static bool IsNamed(Match match, string placeholder)
        => string.Equals(match.Groups["name"].Value, placeholder.Trim('{', '}'), StringComparison.OrdinalIgnoreCase);

    private static string Linked(string line)
    {
        var html = new StringBuilder();
        var at = 0;

        foreach (Match match in WebAddress().Matches(line))
        {
            var address = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', '\'', '"');
            html.Append(WebUtility.HtmlEncode(line[at..match.Index]));
            html.Append("<a href=\"").Append(WebUtility.HtmlEncode(address)).Append("\">").Append(WebUtility.HtmlEncode(address)).Append("</a>");
            at = match.Index + address.Length;
        }

        return html.Append(WebUtility.HtmlEncode(line[at..])).ToString();
    }

    [GeneratedRegex(@"\{\{\s*(?<name>content|subject|footer|preheader)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\n[ \t]*\n\s*")]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex WebAddress();
}
