using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

public interface ISupportEventLedgerCollection : IDiskRepositoryCollection<SupportEventLedgerEntity>;

/// <summary>
/// Collection of handled inbound events. Default collection name <c>SupportEventLedger</c>.
/// </summary>
/// <remarks>
/// <b>The unique index is the deduplication mechanism, not an optimisation.</b> It is what makes recording an
/// event atomic across instances: two instances handed the same retry both attempt the insert, and exactly
/// one succeeds. A read-then-write would let both through at the moment it matters most.
/// <para>
/// The TTL index expires entries after the retention window, because a ledger that grows forever to guard
/// against retries that stopped an hour ago is pure cost.
/// </para>
/// </remarks>
public class SupportEventLedgerCollection : DiskRepositoryCollectionBase<SupportEventLedgerEntity>, ISupportEventLedgerCollection
{
    private readonly string _collectionName;
    private readonly TimeSpan _retention;

    public SupportEventLedgerCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SupportEventLedgerCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.SupportEventLedgerCollectionName ?? "SupportEventLedger";
        _retention = options?.Value.SupportEventLedgerRetention ?? TimeSpan.FromHours(24);
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<SupportEventLedgerEntity>> Indices =>
    [
        new(Builders<SupportEventLedgerEntity>.IndexKeys
                .Ascending(x => x.Source)
                .Ascending(x => x.EventId),
            new CreateIndexOptions { Unique = true, Name = "Source_EventId" }),
        new(Builders<SupportEventLedgerEntity>.IndexKeys.Ascending(x => x.HandledAt),
            new CreateIndexOptions { ExpireAfter = _retention, Name = "HandledAt_ttl" })
    ];
}
