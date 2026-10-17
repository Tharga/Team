using System.Globalization;
using MimeKit;

namespace Tharga.Team.Email;

/// <summary>
/// Turns a file name a sender supplied into one that is safe to store and show.
/// </summary>
/// <remarks>
/// <b>A file name is data from a stranger.</b> It can carry a path that climbs out of a folder, control
/// characters that rewrite a log line, or a right-to-left override that makes an executable read as a PDF.
/// What is kept is the last path segment, stripped of all of those, cut to a sensible length with its extension
/// intact, and a name made from the content type when nothing is left.
/// </remarks>
internal static class AttachmentFileName
{
    /// <summary>The longest name kept, in characters.</summary>
    internal const int MaxLength = 200;

    private const int MaxExtensionLength = 20;
    private const string FallbackStem = "attachment";

    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>A safe name for a file declared as <paramref name="fileName"/> with <paramref name="contentType"/>.</summary>
    public static string Sanitize(string fileName, string contentType)
    {
        var lastSegment = (fileName ?? string.Empty).Replace('\\', '/');
        lastSegment = lastSegment[(lastSegment.LastIndexOf('/') + 1)..];

        var kept = new string([.. lastSegment.Where(IsKept)]).Trim().Trim('.').Trim();

        if (kept.Length == 0) return FallbackStem + ExtensionFor(contentType);

        return kept.Length <= MaxLength ? kept : Shorten(kept);
    }

    /// <summary>A safe name that ends in <paramref name="extension"/>, adding it when the sender's name does not.</summary>
    public static string SanitizeWithExtension(string fileName, string contentType, string extension)
    {
        var name = Sanitize(fileName, contentType);
        if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name;

        var stem = name.Length + extension.Length <= MaxLength ? name : name[..(MaxLength - extension.Length)];
        return stem + extension;
    }

    private static bool IsKept(char c)
        => !char.IsControl(c)
           && !Forbidden.Contains(c)
           && CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.OtherNotAssigned);

    private static string Shorten(string name)
    {
        var extension = Path.GetExtension(name);
        if (extension.Length is 0 or > MaxExtensionLength) return name[..MaxLength];

        return name[..(MaxLength - extension.Length)].TrimEnd() + extension;
    }

    private static string ExtensionFor(string contentType)
    {
        var mediaType = ContentType.TryParse(contentType ?? string.Empty, out var parsed) ? parsed.MimeType : null;

        return mediaType != null && MimeTypes.TryGetExtension(mediaType, out var extension) ? extension : string.Empty;
    }
}
