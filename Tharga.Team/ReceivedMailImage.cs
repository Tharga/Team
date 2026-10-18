namespace Tharga.Team;

/// <summary>
/// An image a received mail shows inside its text, carried with the text so a reader sees it in place.
/// </summary>
public record ReceivedMailImage
{
    /// <summary>The Content-ID the formatting names the image by as <c>cid:</c>, without angle brackets.</summary>
    public required string ContentId { get; init; }

    /// <summary>The declared type, one of the image types a browser shows without running anything.</summary>
    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}
