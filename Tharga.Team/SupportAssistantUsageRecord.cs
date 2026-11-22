namespace Tharga.Team;

/// <summary>
/// One time the support assistant was asked: who asked, in which case, which model answered, and what it took.
/// </summary>
/// <remarks>
/// <b>Never what anybody wrote.</b> Not the question, not the answer, not a tool's arguments: a support conversation is
/// free-form text and where a password gets pasted, and a usage record is read by whoever bills, not by support.
/// <para>
/// Kept whether the assistant answered or declined, because a decline spent the tokens too.
/// </para>
/// </remarks>
public sealed record SupportAssistantUsageRecord
{
    /// <summary>The team the case belongs to.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The case the assistant was asked in.</summary>
    public required string CaseId { get; init; }

    /// <summary>The identity of whoever the assistant was answering; null when there was no caller.</summary>
    public string AskedBy { get; init; }

    /// <summary>Who served the answer, as the chat client names its provider; null when it does not say.</summary>
    public string Provider { get; init; }

    /// <summary>The model that answered; null when neither the provider nor the client says.</summary>
    public string Model { get; init; }

    /// <summary>The tokens the model read.</summary>
    public long InputTokens { get; init; }

    /// <summary>Of <see cref="InputTokens"/>, those read from the provider's cache.</summary>
    public long CachedInputTokens { get; init; }

    /// <summary>Of <see cref="InputTokens"/>, those the provider wrote to its cache.</summary>
    public long CacheWriteTokens { get; init; }

    /// <summary>The tokens the model wrote.</summary>
    public long OutputTokens { get; init; }

    /// <summary>All of them, as the provider totalled them.</summary>
    public long TotalTokens { get; init; }

    /// <summary>How many times the assistant called a service to answer.</summary>
    public int ToolCalls { get; init; }

    /// <summary>Whether it answered; false when it declined.</summary>
    public bool Answered { get; init; }

    /// <summary>How long the assistant took, from being asked to having answered or declined.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>When it finished, in UTC.</summary>
    public DateTime At { get; init; }

    /// <summary>The model's price per million input tokens when the answer was given, from the platform's catalogue; null when none was known (a team's own provider, or before prices were recorded).</summary>
    public decimal? PriceInput { get; init; }

    /// <summary>The price per million cached input tokens at the time; null when none was given.</summary>
    public decimal? PriceCachedInput { get; init; }

    /// <summary>The price per million cache-write tokens at the time; null when none was given, in which case writes are priced as ordinary input.</summary>
    public decimal? PriceCacheWrite { get; init; }

    /// <summary>The price per million output tokens at the time.</summary>
    public decimal? PriceOutput { get; init; }

    /// <summary>The currency of the prices and of <see cref="Cost"/>; null when unpriced.</summary>
    public string Currency { get; init; }

    /// <summary>What the answer cost at the prices it was given at, so a later price change never rewrites it; null when unpriced.</summary>
    public decimal? Cost { get; init; }

    /// <summary>
    /// The cost of a number of tokens at a price per million: input that was not cached at the input price, cached input at the cached
    /// price (the input price when none is given), and output at the output price. Null when no price is known.
    /// </summary>
    public static decimal? CostOf(long inputTokens, long cachedInputTokens, long outputTokens, decimal? priceInput, decimal? priceCachedInput, decimal? priceOutput)
        => CostOf(inputTokens, cachedInputTokens, 0, outputTokens, priceInput, priceCachedInput, null, priceOutput);

    /// <summary>
    /// The cost with cache writes priced apart: input that was neither read from nor written to the cache at the input price, cache reads
    /// at the cached price (the input price when none is given), cache writes at the write price (the input price when none is given),
    /// and output at the output price. Input includes both cache counts, as the providers report it. Null when no price is known.
    /// </summary>
    public static decimal? CostOf(long inputTokens, long cachedInputTokens, long cacheWriteTokens, long outputTokens, decimal? priceInput, decimal? priceCachedInput, decimal? priceCacheWrite, decimal? priceOutput)
    {
        if (priceInput == null && priceCachedInput == null && priceCacheWrite == null && priceOutput == null) return null;

        var input = Math.Max(0, inputTokens);
        var cached = Math.Clamp(cachedInputTokens, 0, input);
        var written = Math.Clamp(cacheWriteTokens, 0, input - cached);
        var fresh = input - cached - written;

        return (fresh * (priceInput ?? 0m)
                + cached * (priceCachedInput ?? priceInput ?? 0m)
                + written * (priceCacheWrite ?? priceInput ?? 0m)
                + Math.Max(0, outputTokens) * (priceOutput ?? 0m)) / 1_000_000m;
    }
}
