namespace Tharga.Team;

/// <summary>A file to attach to a support message, read once from <see cref="Content"/>, which the caller disposes after the call.</summary>
public record SupportAttachmentUpload
{
    /// <summary>The file name as the sender gave it; it is sanitized before it is kept.</summary>
    public required string FileName { get; init; }

    /// <summary>The content type as the sender declared it; it is kept as data only.</summary>
    public required string ContentType { get; init; }

    /// <summary>The content, read from its current position and measured against the limits as it is read.</summary>
    public required Stream Content { get; init; }

    /// <summary>Whether the mail showed the file inside its body; always false for a file uploaded on the site.</summary>
    public bool Inline { get; init; }

    /// <summary>The mail part's Content-ID; always null for a file uploaded on the site.</summary>
    public string ContentId { get; init; }
}
