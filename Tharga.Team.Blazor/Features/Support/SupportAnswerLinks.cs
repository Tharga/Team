using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// Keeps the links an assistant writes only when they stay on this site; any other is shown as its text and address.
/// </summary>
/// <remarks>
/// <b>The model writes these, and a member's own words can steer it.</b> A link that leads off the site is how a steered
/// answer would send somebody to a page that only looks like this one, so it is shown, not followed. A path on the site,
/// such as <c>/support/abc</c>, stays a link: it can only go where the member could go already.
/// </remarks>
internal static partial class SupportAnswerLinks
{
    [GeneratedRegex(@"\[([^\]\n]*)\]\(([^)\s]*)(?:\s+""[^""\n]*"")?\)")]
    private static partial Regex MarkdownLink();

    /// <summary>The Markdown with every link that leaves the site written out as text.</summary>
    public static string SiteOnly(string markdown)
        => string.IsNullOrEmpty(markdown)
            ? markdown
            : MarkdownLink().Replace(markdown, m => OnSite(m.Groups[2].Value) ? m.Value : $"{m.Groups[1].Value} ({m.Groups[2].Value})");

    /// <summary>A path on this site: starting with one slash, never two or a backslash, which a browser reads as another host.</summary>
    public static bool OnSite(string url)
        => url.Length > 0 && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
