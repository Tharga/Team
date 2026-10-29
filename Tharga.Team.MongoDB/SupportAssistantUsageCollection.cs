using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>One time the support assistant was asked. Never what anybody wrote.</summary>
public record SupportAssistantUsageRecordEntity : EntityBase
{
    public required string TeamKey { get; init; }
    public required string CaseId { get; init; }
    public string AskedBy { get; init; }
    public string Provider { get; init; }
    public string Model { get; init; }
    public long InputTokens { get; init; }
    public long CachedInputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long TotalTokens { get; init; }
    public int ToolCalls { get; init; }
    public bool Answered { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime At { get; init; }
}

public interface ISupportAssistantUsageCollection : IDiskRepositoryCollection<SupportAssistantUsageRecordEntity>
{
    /// <summary>The usage in a period totalled per team, provider and model by the server; for one team when <paramref name="teamKey"/> is given.</summary>
    Task<SupportAssistantUsageTotal[]> TotalAsync(DateTime from, DateTime to, string teamKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// The assistant's usage, one document per time it was asked. Default collection name <c>SupportAssistantUsage</c>.
/// </summary>
/// <remarks>
/// <b>Totalled by the server</b>, so a month of every team's answers is never read into the site to be added up. The
/// period leads the index the totals use; the team has an index of its own for purging.
/// </remarks>
public class SupportAssistantUsageCollection : DiskRepositoryCollectionBase<SupportAssistantUsageRecordEntity>, ISupportAssistantUsageCollection
{
    private static readonly FilterDefinitionBuilder<SupportAssistantUsageRecordEntity> Filter = Builders<SupportAssistantUsageRecordEntity>.Filter;

    private readonly string _collectionName;

    public SupportAssistantUsageCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SupportAssistantUsageCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.SupportAssistantUsageCollectionName ?? "SupportAssistantUsage";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<SupportAssistantUsageRecordEntity>> Indices =>
    [
        new(Builders<SupportAssistantUsageRecordEntity>.IndexKeys.Ascending(x => x.At).Ascending(x => x.TeamKey),
            new CreateIndexOptions { Name = "At_TeamKey" }),
        new(Builders<SupportAssistantUsageRecordEntity>.IndexKeys.Ascending(x => x.TeamKey),
            new CreateIndexOptions { Name = "TeamKey" })
    ];

    public Task<SupportAssistantUsageTotal[]> TotalAsync(DateTime from, DateTime to, string teamKey, CancellationToken cancellationToken = default)
        => ExecuteAsync(nameof(TotalAsync), async (collection, _, ct) =>
        {
            var rows = await collection.Aggregate(Totals(from, to, teamKey), cancellationToken: ct).ToListAsync(ct);
            SupportAssistantUsageTotal[] totals = [.. rows.Select(ToTotal)];
            return (totals, totals.Length);
        }, Operation.Read, cancellationToken, Period(from, to, teamKey));

    /// <summary>The records from <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive), for one team when given.</summary>
    internal static FilterDefinition<SupportAssistantUsageRecordEntity> Period(DateTime from, DateTime to, string teamKey)
    {
        var period = Filter.Gte(x => x.At, from) & Filter.Lt(x => x.At, to);
        return string.IsNullOrEmpty(teamKey) ? period : period & Filter.Eq(x => x.TeamKey, teamKey);
    }

    /// <summary>The period's records grouped by team, provider and model, with answers and declines counted apart.</summary>
    internal static PipelineDefinition<SupportAssistantUsageRecordEntity, BsonDocument> Totals(DateTime from, DateTime to, string teamKey)
    {
        static BsonDocument Sum(string field) => new("$sum", "$" + field);
        static BsonDocument Count(bool answered) => new("$sum", new BsonDocument("$cond", new BsonArray { "$" + nameof(SupportAssistantUsageRecordEntity.Answered), answered ? 1 : 0, answered ? 0 : 1 }));

        IPipelineStageDefinition[] stages =
        [
            PipelineStageDefinitionBuilder.Match(Period(from, to, teamKey)),
            new BsonDocumentPipelineStageDefinition<SupportAssistantUsageRecordEntity, BsonDocument>(new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "t", "$" + nameof(SupportAssistantUsageRecordEntity.TeamKey) }, { "p", "$" + nameof(SupportAssistantUsageRecordEntity.Provider) }, { "m", "$" + nameof(SupportAssistantUsageRecordEntity.Model) } } },
                { "answers", Count(true) },
                { "declines", Count(false) },
                { "toolCalls", Sum(nameof(SupportAssistantUsageRecordEntity.ToolCalls)) },
                { "input", Sum(nameof(SupportAssistantUsageRecordEntity.InputTokens)) },
                { "cached", Sum(nameof(SupportAssistantUsageRecordEntity.CachedInputTokens)) },
                { "output", Sum(nameof(SupportAssistantUsageRecordEntity.OutputTokens)) },
                { "total", Sum(nameof(SupportAssistantUsageRecordEntity.TotalTokens)) }
            }))
        ];

        return new PipelineStagePipelineDefinition<SupportAssistantUsageRecordEntity, BsonDocument>(stages);
    }

    /// <summary>One row of <see cref="Totals"/>.</summary>
    internal static SupportAssistantUsageTotal ToTotal(BsonDocument row)
    {
        var key = row["_id"].AsBsonDocument;
        static string Text(BsonDocument document, string name) => document.TryGetValue(name, out var value) && value.IsString ? value.AsString : null;

        return new SupportAssistantUsageTotal
        {
            TeamKey = Text(key, "t"),
            Provider = Text(key, "p"),
            Model = Text(key, "m"),
            Answers = row["answers"].ToInt64(),
            Declines = row["declines"].ToInt64(),
            ToolCalls = row["toolCalls"].ToInt64(),
            InputTokens = row["input"].ToInt64(),
            CachedInputTokens = row["cached"].ToInt64(),
            OutputTokens = row["output"].ToInt64(),
            TotalTokens = row["total"].ToInt64()
        };
    }
}
