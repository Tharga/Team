namespace Tharga.Team;

/// <summary>Which attachments may open in the browser — only types shown without running script, since a file opened from the application runs in its origin — and what every other one is served as.</summary>
public static class SupportAttachmentContentTypes
{
    /// <summary>What a file that must only be saved is served as.</summary>
    public const string Download = "application/octet-stream";

    private static readonly HashSet<string> Viewable = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "text/plain"
    };

    /// <summary>Whether a file of this declared type may open in the browser; parameters such as a charset are ignored.</summary>
    public static bool OpensInBrowser(string contentType) => Viewable.Contains(MediaType(contentType));

    /// <summary>The type to serve a file as: its declared type when it may open in the browser, otherwise <see cref="Download"/>.</summary>
    public static string ServedAs(string contentType) => OpensInBrowser(contentType) ? contentType.Trim() : Download;

    /// <summary>The media type alone, lower-cased and without parameters; empty for none.</summary>
    public static string MediaType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return string.Empty;

        var separator = contentType.IndexOf(';');
        return (separator < 0 ? contentType : contentType[..separator]).Trim().ToLowerInvariant();
    }
}
