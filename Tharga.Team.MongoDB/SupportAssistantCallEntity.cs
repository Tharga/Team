using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>A service call the assistant made for an answer: the tool, and what it was asked for.</summary>
public record SupportAssistantCallEntity
{
    public required string Name { get; init; }

    [BsonIgnoreIfNull]
    public string Arguments { get; init; }
}
