using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>The stored form of <see cref="MailEnvelope"/>.</summary>
public record MailEnvelopeEntity
{
    public required string MessageId { get; init; }

    public required string From { get; init; }

    [BsonIgnoreIfNull]
    public string FromName { get; init; }

    public string[] To { get; init; } = [];

    public string[] Cc { get; init; } = [];

    public required string DeliveredTo { get; init; }

    [BsonIgnoreIfNull]
    public string Subject { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? SentAt { get; init; }

    [BsonIgnoreIfNull]
    public string InReplyTo { get; init; }

    public string[] References { get; init; } = [];

    public bool HasAttachments { get; init; }

    public bool IsAutomated { get; init; }
}
