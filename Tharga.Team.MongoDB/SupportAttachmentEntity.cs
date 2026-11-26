using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>A file a transcript entry carries, by id with what it was called; the content is kept elsewhere.</summary>
/// <remarks>Whether a reader sees a secure file withheld is decided as it is read, so it is never stored.</remarks>
public record SupportAttachmentEntity
{
    public required string Id { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long Size { get; init; }

    [BsonIgnoreIfDefault]
    public bool Inline { get; init; }

    [BsonIgnoreIfNull]
    public string ContentId { get; init; }

    /// <summary>For support's staff only; stored only when set, so the sweep's sparse index holds only the cases that carry one.</summary>
    [BsonIgnoreIfDefault]
    public bool Secure { get; init; }

    [BsonIgnoreIfNull]
    public string UploadedBy { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? RemovedAt { get; init; }
}
