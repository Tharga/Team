using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>One mail key inside a team's <see cref="TeamMailKeysEntity"/>.</summary>
public record TeamMailKeyEntry
{
    public required string Key { get; init; }

    [BsonRepresentation(BsonType.String)]
    public required MailKeyRole Role { get; init; }

    public required DateTime CreatedAt { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? RetiresAt { get; init; }
}
