using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Audit logger that writes to MongoDB via a background channel for zero-latency impact.
/// Resolves IAuditRepositoryCollection lazily to avoid DI issues in test environments.
/// </summary>
public class MongoDbAuditLogger : BackgroundService, IAuditLogger
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MongoDbAuditLogger> _logger;
    private readonly AuditOptions _options;
    private readonly Channel<AuditEntry> _channel;

    public MongoDbAuditLogger(
        IServiceProvider serviceProvider,
        ILogger<MongoDbAuditLogger> logger,
        IOptions<AuditOptions> options = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options?.Value ?? new AuditOptions();
        _channel = Channel.CreateBounded<AuditEntry>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public void Log(AuditEntry entry)
    {
        _channel.Writer.TryWrite(entry);
    }

    public async Task<AuditQueryResult> QueryAsync(AuditQuery query)
    {
        var collection = _serviceProvider.GetService<IAuditRepositoryCollection>();
        if (collection == null) return new AuditQueryResult();

        var options = new MongoDB.Options<AuditEntryEntity>
        {
            Sort = BuildSort(query),
            Skip = query.Skip,
            Limit = query.Take
        };

        var result = await collection.GetManyAsync(BuildFilter(query), options);

        return new AuditQueryResult
        {
            Items = result.Items.Select(ToAuditEntry).ToList(),
            TotalCount = result.TotalCount
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var collection = _serviceProvider.GetService<IAuditRepositoryCollection>();
        if (collection == null)
        {
            _logger.LogWarning("IAuditRepositoryCollection not registered, MongoDB audit logging disabled");
            return;
        }

        var batch = new List<AuditEntryEntity>();
        var flushInterval = TimeSpan.FromSeconds(_options.FlushIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(flushInterval);

                while (batch.Count < _options.BatchSize)
                {
                    try
                    {
                        var entry = await _channel.Reader.ReadAsync(cts.Token);
                        batch.Add(ToEntity(entry));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                if (batch.Count > 0)
                {
                    foreach (var entity in batch)
                    {
                        await collection.AddAsync(entity);
                    }
                    batch.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing audit batch to MongoDB");
                batch.Clear();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal static FilterDefinition<AuditEntryEntity> BuildFilter(AuditQuery query)
    {
        var builder = Builders<AuditEntryEntity>.Filter;
        var filters = new List<FilterDefinition<AuditEntryEntity>>();

        // Multi-value filters take precedence over single-value
        if (query.TeamKeys is { Length: > 0 })
            filters.Add(builder.In(e => e.TeamKey, query.TeamKeys));
        else if (query.TeamKey != null)
            filters.Add(builder.Eq(e => e.TeamKey, query.TeamKey));

        if (query.Features is { Length: > 0 })
            filters.Add(builder.In(e => e.Feature, query.Features));
        else if (query.Feature != null)
            filters.Add(builder.Eq(e => e.Feature, query.Feature));

        if (query.Actions is { Length: > 0 })
            filters.Add(builder.In(e => e.Action, query.Actions));
        else if (query.Action != null)
            filters.Add(builder.Eq(e => e.Action, query.Action));

        if (query.Scopes is { Length: > 0 })
            filters.Add(builder.In(e => e.ScopeChecked, query.Scopes));

        // Nin keeps entries with no ScopeChecked at all, which is what makes excluding one noisy scope
        // leave the consumer-written entries beside it in place.
        if (query.ExcludedScopes is { Length: > 0 })
            filters.Add(builder.Nin(e => e.ScopeChecked, query.ExcludedScopes));

        if (query.EventTypes is { Length: > 0 })
            filters.Add(builder.In(e => e.EventType, query.EventTypes));
        else if (query.EventType != null)
            filters.Add(builder.Eq(e => e.EventType, query.EventType.Value));

        if (query.ExcludedEventTypes is { Length: > 0 })
            filters.Add(builder.Nin(e => e.EventType, query.ExcludedEventTypes));

        if (query.CallerIdentity != null)
            filters.Add(builder.Regex(e => e.CallerIdentity, new BsonRegularExpression(query.CallerIdentity, "i")));

        if (query.CallerKeyId != null)
            filters.Add(builder.Eq(e => e.CallerKeyId, query.CallerKeyId));

        if (query.CallerUserIdentity != null)
            filters.Add(builder.Eq(e => e.CallerUserIdentity, query.CallerUserIdentity));

        if (query.MethodName != null)
            filters.Add(builder.Regex(e => e.MethodName, new BsonRegularExpression(query.MethodName, "i")));

        if (query.CallerType != null)
            filters.Add(builder.Eq(e => e.CallerType, query.CallerType.Value));

        if (query.CallerSource != null)
            filters.Add(builder.Eq(e => e.CallerSource, query.CallerSource.Value));

        if (query.Success != null)
            filters.Add(builder.Eq(e => e.Success, query.Success.Value));

        if (query.From != null)
            filters.Add(builder.Gte(e => e.Timestamp, query.From.Value));

        if (query.To != null)
            filters.Add(builder.Lte(e => e.Timestamp, query.To.Value));

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }

    private static SortDefinition<AuditEntryEntity> BuildSort(AuditQuery query)
    {
        var field = query.SortField switch
        {
            nameof(AuditEntry.Timestamp) => nameof(AuditEntryEntity.Timestamp),
            nameof(AuditEntry.CallerIdentity) => nameof(AuditEntryEntity.CallerIdentity),
            nameof(AuditEntry.MethodName) => nameof(AuditEntryEntity.MethodName),
            nameof(AuditEntry.DurationMs) => nameof(AuditEntryEntity.DurationMs),
            _ => nameof(AuditEntryEntity.Timestamp)
        };

        return query.SortDescending
            ? Builders<AuditEntryEntity>.Sort.Descending(field)
            : Builders<AuditEntryEntity>.Sort.Ascending(field);
    }

    private static AuditEntryEntity ToEntity(AuditEntry entry)
    {
        return new AuditEntryEntity
        {
            Id = ObjectId.GenerateNewId(),
            Timestamp = entry.Timestamp,
            CorrelationId = entry.CorrelationId,
            EventType = entry.EventType,
            Feature = entry.Feature,
            Action = entry.Action,
            MethodName = entry.MethodName,
            DurationMs = entry.DurationMs,
            Success = entry.Success,
            ErrorMessage = entry.ErrorMessage,
            CallerType = entry.CallerType,
            CallerIdentity = entry.CallerIdentity,
            CallerKeyId = entry.CallerKeyId,
            CallerUserIdentity = entry.CallerUserIdentity,
            TeamKey = entry.TeamKey,
            AccessLevel = entry.AccessLevel,
            CallerSource = entry.CallerSource,
            ScopeChecked = entry.ScopeChecked,
            ScopeResult = entry.ScopeResult,
            Metadata = entry.Metadata,
        };
    }

    private static AuditEntry ToAuditEntry(AuditEntryEntity entity)
    {
        return new AuditEntry
        {
            Timestamp = entity.Timestamp,
            CorrelationId = entity.CorrelationId,
            EventType = entity.EventType,
            Feature = entity.Feature,
            Action = entity.Action,
            MethodName = entity.MethodName,
            DurationMs = entity.DurationMs,
            Success = entity.Success,
            ErrorMessage = entity.ErrorMessage,
            CallerType = entity.CallerType,
            CallerIdentity = entity.CallerIdentity,
            CallerKeyId = entity.CallerKeyId,
            CallerUserIdentity = entity.CallerUserIdentity,
            TeamKey = entity.TeamKey,
            AccessLevel = entity.AccessLevel,
            CallerSource = entity.CallerSource,
            ScopeChecked = entity.ScopeChecked,
            ScopeResult = entity.ScopeResult,
            Metadata = entity.Metadata,
        };
    }
}
