namespace Tharga.Team;

/// <summary>
/// A received mail made readable: its text, its formatting made safe, the images it shows in place and the files it
/// carries.
/// </summary>
/// <remarks>
/// <b>Read from the raw message every time</b>, so nothing here is stored. The formatting has been through the one
/// sanitizer — formatting, web and mail links, and images only by <c>cid:</c> reference, a remote image becoming its
/// alt text — and a view sanitizes it again when it renders it.
/// </remarks>
public record ReceivedMailBody
{
    /// <summary>The largest image <see cref="Images"/> carries; a larger one is only listed with the files.</summary>
    public const long MaxInlineImageBytes = 2L * 1024 * 1024;

    public required string MailId { get; init; }

    /// <summary>The text part, or the formatting rendered as text when the mail has none; empty when it has neither.</summary>
    public required string Text { get; init; }

    /// <summary>The formatting, sanitized, with <c>cid:</c> image references kept; null when the mail has none or nothing readable remains.</summary>
    public string Html { get; init; }

    /// <summary>Every file the mail carries, in order: attachments, images shown inside it, and forwarded mail as <c>.eml</c>.</summary>
    /// <remarks>Each <see cref="SupportAttachment.Id"/> is the file's position in the mail, counted from 1.</remarks>
    public SupportAttachment[] Attachments { get; init; } = [];

    /// <summary>
    /// The images <see cref="Html"/> names by Content-ID, of a type a browser shows without running anything and no
    /// larger than <see cref="MaxInlineImageBytes"/>, to show in their place.
    /// </summary>
    public ReceivedMailImage[] Images { get; init; } = [];
}
