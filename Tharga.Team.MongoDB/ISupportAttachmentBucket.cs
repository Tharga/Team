namespace Tharga.Team.MongoDB;

/// <summary>
/// Where <see cref="MongoSupportAttachmentStore"/> keeps file content: GridFS in a host, memory in a test.
/// </summary>
internal interface ISupportAttachmentBucket
{
    /// <summary>Stores the content with its description and returns the new file's id.</summary>
    Task<string> UploadAsync(SupportAttachmentFile file, Stream content, CancellationToken cancellationToken);

    /// <summary>The stored file with this id, or null.</summary>
    Task<SupportAttachmentFile> FindAsync(string id, CancellationToken cancellationToken);

    /// <summary>Every file stored for a case.</summary>
    Task<SupportAttachmentFile[]> FindByCaseAsync(string caseId, CancellationToken cancellationToken);

    /// <summary>A stream over the stored content of the file with this id.</summary>
    Task<Stream> OpenAsync(string id, CancellationToken cancellationToken);

    /// <summary>Removes the file with this id.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}

/// <summary>A stored file as the bucket describes it; <see cref="Id"/> and <see cref="Length"/> are the bucket's to set.</summary>
internal sealed record SupportAttachmentFile
{
    public string Id { get; init; }

    public required string CaseId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public long Length { get; init; }

    public bool Inline { get; init; }

    public string ContentId { get; init; }
}
