namespace Tharga.Team.MongoDB;

/// <summary>The tokens a model reported for an answer.</summary>
public record SupportAssistantUsageEntity
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens { get; init; }
}
