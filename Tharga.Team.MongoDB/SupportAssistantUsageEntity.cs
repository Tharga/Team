namespace Tharga.Team.MongoDB;

/// <summary>The tokens a model reported for an answer.</summary>
public record SupportAssistantUsageEntity
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens { get; init; }
    public long CachedInputTokens { get; init; }

    /// <summary>Absent, which reads as 0, on an answer kept before cache writes were counted.</summary>
    public long CacheWriteTokens { get; init; }
    public string Provider { get; init; }
    public string Model { get; init; }

    /// <summary>How long the assistant took, in milliseconds; absent on an answer kept before it was recorded.</summary>
    [global::MongoDB.Bson.Serialization.Attributes.BsonIgnoreIfNull]
    public long? ElapsedMs { get; init; }
}
