namespace Tharga.Team;

/// <summary>
/// Keeps the support assistant's usage records, and totals them.
/// </summary>
/// <remarks>
/// A <see cref="ISupportAssistantUsageSink"/> that can also be read. <c>AddThargaTeamRepository</c> in
/// <c>Tharga.Team.MongoDB</c> registers one, as both.
/// </remarks>
public interface ISupportAssistantUsageStore : ISupportAssistantUsageSink
{
    /// <summary>
    /// The usage from <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive), totalled per team,
    /// provider and model; for one team when <paramref name="teamKey"/> is given.
    /// </summary>
    Task<SupportAssistantUsageTotal[]> GetTotalsAsync(DateTime from, DateTime to, string teamKey = null, CancellationToken cancellationToken = default);

    /// <summary>Removes every record of a team, when the team is purged; returns how many.</summary>
    Task<long> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the support assistant's usage, for the staff who run the product or bill it.
/// </summary>
/// <remarks>
/// Authorized by <see cref="SystemSupportScopes.UsageRead"/>, which no level and no role holds by default. It says
/// how much every team used and who asked, so it is a decision about the product, not an access level's.
/// </remarks>
public interface ISupportAssistantUsageService
{
    /// <summary>
    /// The usage from <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive), totalled per team,
    /// provider and model; for one team when <paramref name="teamKey"/> is given. Empty where no store is registered.
    /// </summary>
    Task<SupportAssistantUsageTotal[]> GetUsageAsync(DateTime from, DateTime to, string teamKey = null, CancellationToken cancellationToken = default);
}

/// <summary>What one team used of one model in a period.</summary>
public sealed record SupportAssistantUsageTotal
{
    /// <summary>The team.</summary>
    public required string TeamKey { get; init; }

    /// <summary>Who served it; null when the provider did not say.</summary>
    public string Provider { get; init; }

    /// <summary>The model; null when it was not named.</summary>
    public string Model { get; init; }

    /// <summary>How many times the assistant answered.</summary>
    public long Answers { get; init; }

    /// <summary>How many times it declined, having spent tokens all the same.</summary>
    public long Declines { get; init; }

    /// <summary>How many times it called a service.</summary>
    public long ToolCalls { get; init; }

    /// <summary>The tokens the model read.</summary>
    public long InputTokens { get; init; }

    /// <summary>Of <see cref="InputTokens"/>, those read from the provider's cache.</summary>
    public long CachedInputTokens { get; init; }

    /// <summary>The tokens the model wrote.</summary>
    public long OutputTokens { get; init; }

    /// <summary>All of them, as the provider totalled them.</summary>
    public long TotalTokens { get; init; }
}
