using MimeKit;

namespace Tharga.Team.Email;

/// <summary>
/// A declared content type made fit to keep: parsed, without the parameters that can carry a file name, and
/// <c>application/octet-stream</c> when it says nothing usable.
/// </summary>
/// <remarks>
/// Only the charset survives, because it is what a plain-text file needs to read right; a <c>name</c>
/// parameter would be a second, unsanitized copy of the file name.
/// </remarks>
internal static class AttachmentContentType
{
    /// <summary>What a file declaring nothing usable is kept as.</summary>
    public const string Unknown = "application/octet-stream";

    /// <summary>The media type, lower-cased, with its charset when it has one.</summary>
    public static string Normalize(string declared)
        => ContentType.TryParse(declared ?? string.Empty, out var parsed) ? Of(parsed) : Unknown;

    /// <summary>The media type of a parsed content type, lower-cased, with its charset when it has one.</summary>
    public static string Of(ContentType contentType)
    {
        if (contentType == null || string.IsNullOrWhiteSpace(contentType.MediaType) || string.IsNullOrWhiteSpace(contentType.MediaSubtype)) return Unknown;

        var mediaType = contentType.MimeType.ToLowerInvariant();
        if (!IsToken(mediaType.Replace("/", string.Empty))) return Unknown;

        var charset = contentType.Charset?.Trim().ToLowerInvariant();

        return string.IsNullOrEmpty(charset) || !IsToken(charset) ? mediaType : $"{mediaType}; charset={charset}";
    }

    private static bool IsToken(string value)
        => value.Length is > 0 and <= 100 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' or '_');
}
