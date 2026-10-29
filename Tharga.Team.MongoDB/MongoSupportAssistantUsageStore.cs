using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// MongoDB implementation of <see cref="ISupportAssistantUsageStore"/>: one document per time the assistant was asked.
/// </summary>
internal sealed class MongoSupportAssistantUsageStore(ISupportAssistantUsageCollection collection) : ISupportAssistantUsageStore
{
    public Task RecordAsync(SupportAssistantUsageRecord record, CancellationToken cancellationToken = default)
        => collection.AddAsync(ToEntity(record));

    public Task<SupportAssistantUsageTotal[]> GetTotalsAsync(DateTime from, DateTime to, string teamKey = null, CancellationToken cancellationToken = default)
        => collection.TotalAsync(from, to, teamKey, cancellationToken);

    public async Task<long> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(teamKey) ? 0 : await collection.DeleteManyAsync(Builders<SupportAssistantUsageRecordEntity>.Filter.Eq(x => x.TeamKey, teamKey));

    internal static SupportAssistantUsageRecordEntity ToEntity(SupportAssistantUsageRecord record) => new()
    {
        TeamKey = record.TeamKey,
        CaseId = record.CaseId,
        AskedBy = record.AskedBy,
        Provider = record.Provider,
        Model = record.Model,
        InputTokens = record.InputTokens,
        CachedInputTokens = record.CachedInputTokens,
        OutputTokens = record.OutputTokens,
        TotalTokens = record.TotalTokens,
        ToolCalls = record.ToolCalls,
        Answered = record.Answered,
        Duration = record.Duration,
        At = record.At
    };

    internal static SupportAssistantUsageRecord ToRecord(SupportAssistantUsageRecordEntity entity) => new()
    {
        TeamKey = entity.TeamKey,
        CaseId = entity.CaseId,
        AskedBy = entity.AskedBy,
        Provider = entity.Provider,
        Model = entity.Model,
        InputTokens = entity.InputTokens,
        CachedInputTokens = entity.CachedInputTokens,
        OutputTokens = entity.OutputTokens,
        TotalTokens = entity.TotalTokens,
        ToolCalls = entity.ToolCalls,
        Answered = entity.Answered,
        Duration = entity.Duration,
        At = entity.At
    };
}

/// <summary>Hands each record to whichever <see cref="ISupportAssistantUsageStore"/> is registered, so the store is also a sink.</summary>
internal sealed class SupportAssistantUsageStoreSink(ISupportAssistantUsageStore store) : ISupportAssistantUsageSink
{
    public Task RecordAsync(SupportAssistantUsageRecord record, CancellationToken cancellationToken = default)
        => store.RecordAsync(record, cancellationToken);
}
