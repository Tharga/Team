using System.Globalization;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>A file size as a reader reads it — bytes, KB or MB, one decimal at most, in the reader's culture.</summary>
internal static class SupportAttachmentSize
{
    private const double Kilobyte = 1024;
    private const double Megabyte = Kilobyte * 1024;

    /// <summary>The size of <paramref name="bytes"/> with its unit.</summary>
    public static string Format(long bytes)
    {
        if (bytes < Kilobyte) return string.Format(CultureInfo.CurrentCulture, "{0} B", bytes);
        if (bytes < Megabyte) return string.Format(CultureInfo.CurrentCulture, "{0:0.#} KB", bytes / Kilobyte);

        return string.Format(CultureInfo.CurrentCulture, "{0:0.#} MB", bytes / Megabyte);
    }
}
