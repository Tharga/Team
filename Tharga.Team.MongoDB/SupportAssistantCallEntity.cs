using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>A service call the assistant made for an answer: the tool, and what it was asked for.</summary>
public record SupportAssistantCallEntity
{
    public required string Name { get; init; }

    [BsonIgnoreIfNull]
    public string Arguments { get; init; }

    /// <summary>Whether the call returned without failing. Absent when not known, and on every call kept before this existed.</summary>
    [BsonIgnoreIfNull]
    public bool? Succeeded { get; init; }

    /// <summary>What the tool handed back, when the host chose to keep it. Absent otherwise, and on every call kept before this existed.</summary>
    [BsonIgnoreIfNull]
    public string Result { get; init; }
}
