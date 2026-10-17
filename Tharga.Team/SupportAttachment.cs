namespace Tharga.Team;

/// <summary>A file carried by a support message, described by its sanitized name, declared type and size; the content is kept by <see cref="ISupportAttachmentStore"/>.</summary>
public record SupportAttachment
{
    /// <summary>The attachment's id within its case, assigned by the store.</summary>
    public required string Id { get; init; }

    /// <summary>The sanitized file name, never empty and never carrying a path.</summary>
    public required string FileName { get; init; }

    /// <summary>The content type the sender declared, kept as data and never trusted to decide how the file is served.</summary>
    public required string ContentType { get; init; }

    /// <summary>The size of the stored content in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>Whether the mail showed the file inside its body rather than as an attachment.</summary>
    public bool Inline { get; init; }

    /// <summary>The mail part's Content-ID without angle brackets, which an HTML body names as <c>cid:</c>; otherwise null.</summary>
    public string ContentId { get; init; }
}
