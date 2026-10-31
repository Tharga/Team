using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Makes the <c>Timestamp_TTL</c> index of an existing audit collection follow
/// <see cref="AuditOptions.RetentionDays"/>. The index is created by name, and an index that already exists
/// is left as it is, so a changed setting never reached a database that had the collection.
/// </summary>
internal sealed class AuditRetentionIndexService : BackgroundService
{
    private readonly IMongoDbServiceFactory _factory;
    private readonly IOptions<AuditOptions> _options;
    private readonly IOptions<DatabaseOptions> _database;
    private readonly ILogger<AuditRetentionIndexService> _logger;

    public AuditRetentionIndexService(
        IMongoDbServiceFactory factory,
        IOptions<AuditOptions> options,
        ILogger<AuditRetentionIndexService> logger,
        IOptions<DatabaseOptions> database = null)
    {
        _factory = factory;
        _options = options;
        _database = database;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_database?.Value?.AssureIndex == AssureIndexMode.Disabled)
        {
            _logger.LogDebug("Index assurance is disabled, so the audit retention index is left to the host.");
            return;
        }

        try
        {
            await ReconcileAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "The {Index} index on {Collection} could not be made to match the audit retention.", AuditRetentionIndex.Name, AuditRetentionIndex.CollectionName);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var wanted = AuditRetention.GetExpireAfter(AuditRetention.ResolveDays(_options));

        var service = _factory.GetMongoDbService(() => new DatabaseContext { CollectionName = AuditRetentionIndex.CollectionName });
        var collection = await service.GetCollectionAsync<BsonDocument>(AuditRetentionIndex.CollectionName);

        using var cursor = await collection.Indexes.ListAsync(cancellationToken);
        var existing = AuditRetentionIndex.Find(await cursor.ToListAsync(cancellationToken));

        switch (AuditRetentionIndex.Decide(existing, wanted))
        {
            case AuditRetentionIndexAction.Create:
                await collection.Indexes.CreateOneAsync(
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys.Ascending(AuditRetentionIndex.TimestampField),
                        new CreateIndexOptions { Name = AuditRetentionIndex.Name, ExpireAfter = wanted }),
                    cancellationToken: cancellationToken);
                _logger.LogInformation("Audit entries are now kept for {Days} days: created the {Index} index on {Collection}.", wanted!.Value.TotalDays, AuditRetentionIndex.Name, AuditRetentionIndex.CollectionName);
                break;
            case AuditRetentionIndexAction.Change:
                await collection.Database.RunCommandAsync<BsonDocument>(AuditRetentionIndex.ChangeCommand(wanted!.Value), cancellationToken: cancellationToken);
                _logger.LogInformation("Audit entries are now kept for {Days} days instead of {OldDays}: changed the {Index} index on {Collection}.", wanted.Value.TotalDays, TimeSpan.FromSeconds(existing.ExpireAfterSeconds ?? 0).TotalDays, AuditRetentionIndex.Name, AuditRetentionIndex.CollectionName);
                break;
            case AuditRetentionIndexAction.Drop:
                await collection.Indexes.DropOneAsync(AuditRetentionIndex.Name, cancellationToken);
                _logger.LogInformation("Audit entries are now kept forever: dropped the {Index} index on {Collection}.", AuditRetentionIndex.Name, AuditRetentionIndex.CollectionName);
                break;
        }
    }
}
