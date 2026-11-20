using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
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

    public Task<(IReadOnlyList<IReadOnlyList<AuditEntryEntity>> Groups, int Total)> GetGroupsAsync(FilterDefinition<AuditEntryEntity> filter, int skip, int take, bool newestFirst, CancellationToken cancellationToken = default)
        => ExecuteAsync(nameof(GetGroupsAsync), async (collection, _, ct) =>
        {
            var rows = await collection.Aggregate(GroupPage(filter, skip, take, newestFirst), cancellationToken: ct).ToListAsync(ct);
            var counted = await collection.Aggregate(GroupCount(filter), cancellationToken: ct).FirstOrDefaultAsync(ct);
            var total = counted != null && counted.TryGetValue("n", out var n) ? n.ToInt32() : 0;
            if (rows.Count == 0) return (((IReadOnlyList<IReadOnlyList<AuditEntryEntity>>)[], total), 0);

            var builder = Builders<AuditEntryEntity>.Filter;
            var correlationIds = rows.Where(x => x.Id.EntryId == null).Select(x => x.Id.CorrelationId).ToArray();
            var entryIds = rows.Where(x => x.Id.EntryId != null).Select(x => x.Id.EntryId.Value).ToArray();
            var members = new List<FilterDefinition<AuditEntryEntity>>();
            if (correlationIds.Length > 0) members.Add(builder.In(x => x.CorrelationId, correlationIds));
            if (entryIds.Length > 0) members.Add(builder.In(x => x.Id, entryIds));

            var entries = await collection
                .Find(builder.And(filter, builder.Or(members)))
                .Sort(Builders<AuditEntryEntity>.Sort.Ascending(x => x.Timestamp))
                .ToListAsync(ct);

            var byCorrelation = entries.Where(x => x.CorrelationId != Guid.Empty).GroupBy(x => x.CorrelationId).ToDictionary(x => x.Key, x => (IReadOnlyList<AuditEntryEntity>)[.. x]);
            var byEntry = entries.ToDictionary(x => x.Id);

            IReadOnlyList<IReadOnlyList<AuditEntryEntity>> groups =
            [
                .. rows
                    .Select(x => x.Id.EntryId != null
                        ? (byEntry.TryGetValue(x.Id.EntryId.Value, out var one) ? [one] : (IReadOnlyList<AuditEntryEntity>)[])
                        : byCorrelation.GetValueOrDefault(x.Id.CorrelationId, []))
                    .Where(x => x.Count > 0)
            ];

            return ((groups, total), groups.Count);
        }, Operation.Read, cancellationToken, filter);

    /// <summary>One row of the grouped read: a unit of work, and when it began.</summary>
    internal sealed class GroupRow
    {
        [BsonId]
        public GroupKey Id { get; set; }

        public DateTime Started { get; set; }
    }

    /// <summary>The correlation id, and the entry itself when it has none and so stands alone.</summary>
    internal sealed class GroupKey
    {
        public Guid CorrelationId { get; set; }

        public ObjectId? EntryId { get; set; }
    }

    /// <summary>
    /// The value an entry with no correlation id holds, as the driver writes it. Taken from the driver rather than built by hand,
    /// because a hand-built binary would carry whichever Guid representation the host did not configure.
    /// </summary>
    internal static BsonValue EmptyCorrelation()
    {
        var rendered = Builders<AuditEntryEntity>.Filter.Eq(x => x.CorrelationId, Guid.Empty)
            .Render(new RenderArgs<AuditEntryEntity>(BsonSerializer.LookupSerializer<AuditEntryEntity>(), BsonSerializer.SerializerRegistry));
        var value = rendered[nameof(AuditEntryEntity.CorrelationId)];

        return value is BsonDocument wrapped && wrapped.Contains("$eq") ? wrapped["$eq"] : value;
    }

    private static BsonDocument UnitOfWork(BsonValue emptyCorrelation) => new("$group", new BsonDocument
    {
        {
            "_id", new BsonDocument
            {
                { nameof(GroupKey.CorrelationId), "$" + nameof(AuditEntryEntity.CorrelationId) },
                { nameof(GroupKey.EntryId), new BsonDocument("$cond", new BsonArray { new BsonDocument("$eq", new BsonArray { "$" + nameof(AuditEntryEntity.CorrelationId), emptyCorrelation }), "$_id", BsonNull.Value }) }
            }
        },
        { nameof(GroupRow.Started), new BsonDocument("$min", "$" + nameof(AuditEntryEntity.Timestamp)) }
    });

    /// <summary>The page of units of work: matching entries grouped by correlation id, ordered by when each began.</summary>
    /// <param name="filter">Which entries to group.</param>
    /// <param name="skip">How many units of work to skip.</param>
    /// <param name="take">How many units of work to return.</param>
    /// <param name="newestFirst">Whether the most recently begun unit of work comes first.</param>
    /// <param name="emptyCorrelation">What an entry with no correlation id holds; the driver's own value unless a test stands one in.</param>
    internal static PipelineDefinition<AuditEntryEntity, GroupRow> GroupPage(FilterDefinition<AuditEntryEntity> filter, int skip, int take, bool newestFirst, BsonValue emptyCorrelation = null)
    {
        var direction = newestFirst ? -1 : 1;
        IPipelineStageDefinition[] stages =
        [
            PipelineStageDefinitionBuilder.Match(filter),
            new BsonDocumentPipelineStageDefinition<AuditEntryEntity, GroupRow>(UnitOfWork(emptyCorrelation ?? EmptyCorrelation())),
            new BsonDocumentPipelineStageDefinition<GroupRow, GroupRow>(new BsonDocument("$sort", new BsonDocument { { nameof(GroupRow.Started), direction }, { "_id." + nameof(GroupKey.CorrelationId), 1 }, { "_id." + nameof(GroupKey.EntryId), 1 } })),
            new BsonDocumentPipelineStageDefinition<GroupRow, GroupRow>(new BsonDocument("$skip", Math.Max(skip, 0))),
            new BsonDocumentPipelineStageDefinition<GroupRow, GroupRow>(new BsonDocument("$limit", Math.Max(take, 1)))
        ];

        return new PipelineStagePipelineDefinition<AuditEntryEntity, GroupRow>(stages);
    }

    /// <summary>How many units of work match, as <c>{ n }</c>.</summary>
    internal static PipelineDefinition<AuditEntryEntity, BsonDocument> GroupCount(FilterDefinition<AuditEntryEntity> filter, BsonValue emptyCorrelation = null)
    {
        IPipelineStageDefinition[] stages =
        [
            PipelineStageDefinitionBuilder.Match(filter),
            new BsonDocumentPipelineStageDefinition<AuditEntryEntity, GroupRow>(UnitOfWork(emptyCorrelation ?? EmptyCorrelation())),
            new BsonDocumentPipelineStageDefinition<GroupRow, BsonDocument>(new BsonDocument("$count", "n"))
        ];

        return new PipelineStagePipelineDefinition<AuditEntryEntity, BsonDocument>(stages);
    }

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

            // The unit of work an entry belongs to: the grouped read finds a page of groups, then the entries of each.
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys.Ascending(x => x.CorrelationId).Ascending(x => x.Timestamp),
                new CreateIndexOptions { Name = "CorrelationId_Timestamp" }));

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

            // What was done to one user, team or key, newest first. Sparse: most entries have no target, and an entry written
            // before targets were recorded has none, so they stay out of the index.
            indices.Add(new(Builders<AuditEntryEntity>.IndexKeys
                    .Ascending(x => x.TargetKind)
                    .Ascending(x => x.TargetKey)
                    .Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "TargetKind_TargetKey_Timestamp", Sparse = true }));

            return indices;
        }
    }
}
