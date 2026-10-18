using AngleSharp.Html.Parser;

namespace Tharga.Team.Email;

/// <summary>The Content-IDs an HTML body's images name with <c>cid:</c>, which decide what travels beside it inline.</summary>
internal static class HtmlContentIds
{
    private const string Scheme = "cid:";

    public static HashSet<string> Of(string html)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(html)) return ids;

        foreach (var image in new HtmlParser().ParseDocument(html).QuerySelectorAll("img[src]"))
        {
            var source = image.GetAttribute("src")?.Trim();
            if (source == null || !source.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) continue;

            var id = Uri.UnescapeDataString(source[Scheme.Length..]).Trim().Trim('<', '>');
            if (id.Length > 0) ids.Add(id);
        }

        return ids;
    }
}
