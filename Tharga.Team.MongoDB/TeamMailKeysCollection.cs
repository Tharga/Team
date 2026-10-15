using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

public interface ITeamMailKeysCollection : IDiskRepositoryCollection<TeamMailKeysEntity>;

public class TeamMailKeysCollection : DiskRepositoryCollectionBase<TeamMailKeysEntity>, ITeamMailKeysCollection
{
    private readonly string _collectionName;

    public TeamMailKeysCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<TeamMailKeysCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.TeamMailKeyCollectionName ?? "TeamMailKey";
    }

    public override string CollectionName => _collectionName;

    /// <remarks>
    /// The key index is unique across documents, so no key is ever held by two teams, retired keys included —
    /// mail to a retired key must never reach a team that happens to draw the same value later.
    /// </remarks>
    public override IEnumerable<CreateIndexModel<TeamMailKeysEntity>> Indices =>
    [
        new(Builders<TeamMailKeysEntity>.IndexKeys.Ascending(x => x.TeamKey), new CreateIndexOptions { Unique = true, Name = "TeamKey" }),
        new(Builders<TeamMailKeysEntity>.IndexKeys.Ascending("Keys.Key"), new CreateIndexOptions { Unique = true, Name = "Keys_Key" })
    ];
}
