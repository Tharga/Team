using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>A file a transcript entry carries, by id with what it was called; the content is kept elsewhere.</summary>
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
}
