using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>Who wrote to a support case through a public form, as they typed it.</summary>
public record SupportExternalContactEntity
{
    [BsonIgnoreIfNull]
    public string Name { get; init; }

    public required string Email { get; init; }

    [BsonIgnoreIfNull]
    public string Phone { get; init; }

    [BsonIgnoreIfNull]
    public string Organisation { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? ConsentGivenAt { get; init; }

    /// <summary>The host's own fields, as pairs: a field name is the host's to choose, and a document key may not hold a dot or start with a dollar sign.</summary>
    [BsonIgnoreIfNull]
    public SupportExternalFieldEntity[] Fields { get; init; }
}

/// <summary>One extra field a public form sent.</summary>
public record SupportExternalFieldEntity
{
    public required string Name { get; init; }

    [BsonIgnoreIfNull]
    public string Value { get; init; }
}
