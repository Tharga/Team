using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>How the person the assistant answered rated the conversation in a case as a whole.</summary>
public record SupportConversationRatingEntity
{
    [BsonRepresentation(BsonType.String)]
    public required SupportAnswerRating Rating { get; init; }

    [BsonIgnoreIfNull]
    public string Comment { get; init; }

    public required DateTime RatedAt { get; init; }

    public required string RatedBy { get; init; }
}
