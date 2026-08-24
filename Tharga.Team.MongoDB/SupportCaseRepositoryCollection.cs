using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

public interface ISupportCaseRepositoryCollection : IDiskRepositoryCollection<SupportCaseEntity>;

/// <summary>
/// MongoDB collection for support cases. Default collection name <c>SupportCase</c> (override via
/// <see cref="ThargaTeamOptions.SupportCaseCollectionName"/>).
/// </summary>
/// <remarks>
/// The team index is not unique and leads on <see cref="SupportCaseEntity.TeamKey"/> rather than on the case
/// id, because every read is scoped by team — a case id alone never identifies a case. The case index is
/// unique on the pair for the same reason: two teams may not share one case id, and a lookup that forgot the
/// team would otherwise still find something.
/// </remarks>
public class SupportCaseRepositoryCollection : DiskRepositoryCollectionBase<SupportCaseEntity>, ISupportCaseRepositoryCollection
{
    private readonly string _collectionName;

    public SupportCaseRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SupportCaseRepositoryCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.SupportCaseCollectionName ?? "SupportCase";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<SupportCaseEntity>> Indices =>
    [
        new(Builders<SupportCaseEntity>.IndexKeys
                .Ascending(x => x.TeamKey)
                .Ascending(x => x.CaseId),
            new CreateIndexOptions { Unique = true, Name = "TeamKey_CaseId" }),
        new(Builders<SupportCaseEntity>.IndexKeys
                .Ascending(x => x.TeamKey)
                .Ascending(x => x.AuthorIdentity)
                .Descending(x => x.CreatedAt),
            new CreateIndexOptions { Name = "TeamKey_Author_CreatedAt" })
    ];
}
