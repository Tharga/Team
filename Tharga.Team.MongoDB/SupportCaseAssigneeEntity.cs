using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>The member of support staff a case is assigned to, as they were named when it was assigned.</summary>
public record SupportCaseAssigneeEntity
{
    /// <summary>The person's stable authentication subject, the same value the author and closer fields hold.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    [BsonIgnoreIfNull]
    public string Email { get; init; }
}
