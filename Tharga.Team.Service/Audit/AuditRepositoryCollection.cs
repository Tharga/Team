using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.Service.Audit;

internal class AuditRepositoryCollection : DiskRepositoryCollectionBase<AuditEntryEntity>, IAuditRepositoryCollection
{
    private readonly int? _retentionDays;

    public AuditRepositoryCollection(
        IMongoDbServiceFactory mongoDbServiceFactory,
        ILogger<AuditRepositoryCollection> logger,
        Microsoft.Extensions.Options.IOptions<AuditOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _retentionDays = AuditRetention.ResolveDays(options);
    }

    public override string CollectionName => AuditRetentionIndex.CollectionName;

    public override IEnumerable<CreateIndexModel<AuditEntryEntity>> Indices
    {
        get
        {
            var indices = new List<CreateIndexModel<AuditEntryEntity>>();

            // TTL index for automatic retention cleanup — omitted when retention is disabled
            // (RetentionDays null / <= 0), in which case entries are kept forever.
            var expireAfter = AuditRetention.GetExpireAfter(_retentionDays);
            if (expireAfter.HasValue)
            {
                indices.Add(new(Builders<AuditEntryEntity>.IndexKeys.Ascending(x => x.Timestamp),
                    new CreateIndexOptions { Name = AuditRetentionIndex.Name, ExpireAfter = expireAfter.Value }));
            }

            // Compound index for default sort (descending time) with team filter
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys
                    .Descending(x => x.Timestamp)
                    .Ascending(x => x.TeamKey),
                new CreateIndexOptions { Name = "Timestamp_desc_TeamKey" }));

            // Individual indices for $in queries from multi-select filters
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys.Ascending(x => x.Feature),
                new CreateIndexOptions { Name = "Feature" }));
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys.Ascending(x => x.Action),
                new CreateIndexOptions { Name = "Action" }));
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys.Ascending(x => x.CallerIdentity),
                new CreateIndexOptions { Name = "CallerIdentity" }));
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys.Ascending(x => x.CallerSource),
                new CreateIndexOptions { Name = "CallerSource" }));
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys.Ascending(x => x.EventType),
                new CreateIndexOptions { Name = "EventType" }));
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys.Ascending(x => x.ScopeChecked),
                new CreateIndexOptions { Name = "ScopeChecked" }));

            return indices;
        }
    }
}
