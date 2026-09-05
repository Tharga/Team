using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>How far this deployment has read the support mailbox.</summary>
/// <remarks>
/// One record per deployment, so this collection holds one document in the ordinary case and two when a
/// mailbox is shared by two sites. It is a bookmark rather than a record of anything: losing it costs a
/// re-read, which the event ledger makes harmless.
/// </remarks>
public record SupportMailPositionEntity : EntityBase
{
    /// <summary>
    /// Identifies the deployment, not the mailbox — two instances sharing a mailbox must not share a
    /// position.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>The mailbox's UID generation when the position was written.</summary>
    public required long UidValidity { get; init; }

    /// <summary>The highest UID considered.</summary>
    public required long LastUid { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

public interface ISupportMailPositionCollection : IDiskRepositoryCollection<SupportMailPositionEntity>;

/// <summary>
/// Collection of mailbox read positions. Default collection name <c>SupportMailPosition</c>.
/// </summary>
/// <remarks>
/// The unique index on <see cref="SupportMailPositionEntity.Key"/> is what keeps it to one record per
/// deployment: the position is written on every poll, and an upsert without it would eventually leave
/// several rows disagreeing about where the mailbox has been read to.
/// </remarks>
public class SupportMailPositionCollection : DiskRepositoryCollectionBase<SupportMailPositionEntity>, ISupportMailPositionCollection
{
    private readonly string _collectionName;

    public SupportMailPositionCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SupportMailPositionCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.SupportMailPositionCollectionName ?? "SupportMailPosition";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<SupportMailPositionEntity>> Indices =>
    [
        new(Builders<SupportMailPositionEntity>.IndexKeys.Ascending(x => x.Key),
            new CreateIndexOptions { Unique = true, Name = "Key" })
    ];
}
