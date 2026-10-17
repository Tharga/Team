using System.Globalization;
using Tharga.Team.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Applies <see cref="SupportAttachmentLimits"/> the same way in both directions: a received mail keeps what
/// fits and says what it left out, a file written on the site is refused with a reason.
/// </summary>
/// <remarks>
/// <b>Received mail is never refused as a whole.</b> The ledger has already recorded it and there is nobody to
/// tell, so the files that fit are kept in the order they came and each one left out is named in the message.
/// </remarks>
internal static class SupportAttachmentLimitCheck
{
    /// <summary>The limits a host configured on its mail options.</summary>
    public static SupportAttachmentLimits LimitsOf(MailOptions options)
        => options == null
            ? SupportAttachmentLimits.Default
            : new SupportAttachmentLimits
            {
                MaxFileBytes = options.MaxAttachmentBytes,
                MaxTotalBytes = options.MaxAttachmentTotalBytes,
                MaxCount = options.MaxAttachmentCount
            };

    /// <summary>The files of a received mail that fit the limits, and a note for each one that does not.</summary>
    public static (IReadOnlyList<InboundAttachment> Kept, IReadOnlyList<string> Notes) Screen(IReadOnlyList<InboundAttachment> files, SupportAttachmentLimits limits)
    {
        var kept = new List<InboundAttachment>();
        var notes = new List<string>();
        long total = 0;

        foreach (var file in files ?? [])
        {
            var size = file.Content?.LongLength ?? 0;

            if (kept.Count >= limits.MaxCount)
                notes.Add($"[{file.FileName} was not stored: one message may carry at most {limits.MaxCount} files.]");
            else if (size > limits.MaxFileBytes)
                notes.Add($"[{file.FileName} was not stored: it is {FormatSize(size)}, more than the {FormatSize(limits.MaxFileBytes)} one file may be.]");
            else if (total + size > limits.MaxTotalBytes)
                notes.Add($"[{file.FileName} was not stored: together with the files before it, it passes the {FormatSize(limits.MaxTotalBytes)} one message may carry.]");
            else
            {
                kept.Add(file);
                total += size;
            }
        }

        return (kept, notes);
    }

    /// <summary>Refuses files written on the site that are too many, or declare a size past the limits.</summary>
    /// <remarks>A size the stream cannot tell is measured while the file is stored instead.</remarks>
    public static void RequireWithin(IReadOnlyList<SupportAttachmentUpload> files, SupportAttachmentLimits limits)
    {
        if (files.Count > limits.MaxCount)
            throw new InvalidOperationException($"A message may carry at most {limits.MaxCount} files, and {files.Count} were attached.");

        long total = 0;

        foreach (var file in files)
        {
            if (file.Content == null) throw new ArgumentException($"The file '{file.FileName}' has no content.", nameof(files));

            var size = DeclaredSize(file.Content);
            if (size == null) continue;

            if (size > limits.MaxFileBytes) throw TooLarge(file.FileName, size.Value, limits);

            total += size.Value;
            if (total > limits.MaxTotalBytes) throw TooLargeTogether(limits);
        }
    }

    /// <summary>The refusal for one file past the size a file may be.</summary>
    public static InvalidOperationException TooLarge(string fileName, long size, SupportAttachmentLimits limits)
        => new($"'{fileName}' is {FormatSize(size)}, more than the {FormatSize(limits.MaxFileBytes)} one file may be.");

    /// <summary>The refusal for files that together pass the size a message may carry.</summary>
    public static InvalidOperationException TooLargeTogether(SupportAttachmentLimits limits)
        => new($"Together the files are more than the {FormatSize(limits.MaxTotalBytes)} one message may carry.");

    /// <summary>A size as a person reads it: bytes, KB or MB, one decimal at most.</summary>
    public static string FormatSize(long bytes)
    {
        const double kilobyte = 1024;
        const double megabyte = kilobyte * 1024;

        if (bytes < kilobyte) return string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes");
        if (bytes < megabyte) return string.Create(CultureInfo.InvariantCulture, $"{bytes / kilobyte:0.#} KB");

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / megabyte:0.#} MB");
    }

    private static long? DeclaredSize(Stream content)
    {
        try
        {
            return content.CanSeek ? content.Length - content.Position : content.Length;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
