using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>An embedded reference to a team a support case names, by key with the name it had then.</summary>
public record SupportCaseTeamEntity
{
    public required string Key { get; init; }

    [BsonIgnoreIfNull]
    public string Name { get; init; }
}
