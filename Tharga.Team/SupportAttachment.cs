namespace Tharga.Team;

/// <summary>A file carried by a support message, described by its sanitized name, declared type and size; the content is kept by <see cref="ISupportAttachmentStore"/>.</summary>
public record SupportAttachment
{
    /// <summary>The attachment's id within its case, assigned by the store.</summary>
    public required string Id { get; init; }

    /// <summary>The sanitized file name, never carrying a path; empty only on a <see cref="Withheld"/> file, and otherwise never.</summary>
    public required string FileName { get; init; }

    /// <summary>The content type the sender declared, kept as data and never trusted to decide how the file is served; empty only on a <see cref="Withheld"/> file.</summary>
    public required string ContentType { get; init; }

    /// <summary>The size of the stored content in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>Whether the mail showed the file inside its body rather than as an attachment.</summary>
    public bool Inline { get; init; }

    /// <summary>The mail part's Content-ID without angle brackets, which an HTML body names as <c>cid:</c>; otherwise null.</summary>
    public string ContentId { get; init; }

    /// <summary>
    /// Whether the file is for the product's support staff only: besides them, only the member who uploaded it may see what it is or open it.
    /// </summary>
    /// <remarks>
    /// A secure file never leaves the application. It is not attached to mail, not posted to any channel and not given to the
    /// assistant, and its content is removed once <c>SupportCaseOptions.SecureFiles.KeepFor</c> has passed.
    /// </remarks>
    public bool Secure { get; init; }

    /// <summary>The authentication subject of the member who uploaded a <see cref="Secure"/> file; null for any other file.</summary>
    public string UploadedBy { get; init; }

    /// <summary>
    /// Whether this reader may not see the <see cref="Secure"/> file: <see cref="FileName"/> and <see cref="ContentType"/> are blank
    /// and opening it is refused. Set as the file is read, never stored.
    /// </summary>
    public bool Withheld { get; init; }

    /// <summary>When a <see cref="Secure"/> file's content was removed, because its time ran out or support removed it earlier; null while it can still be opened.</summary>
    public DateTime? RemovedAt { get; init; }
}
