using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

public interface IIconSettingsRepositoryCollection : IDiskRepositoryCollection<IconSettingsEntity>;

/// <summary>
/// MongoDB collection for the site's icon settings. Default collection name <c>IconSettings</c> (override via
/// <see cref="ThargaTeamOptions.IconSettingsCollectionName"/>), with a unique index on
/// <see cref="IconSettingsEntity.Key"/> — which is what keeps "there is one of these" true in the store rather
/// than only in the code that writes it.
/// </summary>
public class IconSettingsRepositoryCollection : DiskRepositoryCollectionBase<IconSettingsEntity>, IIconSettingsRepositoryCollection
{
    private readonly string _collectionName;

    public IconSettingsRepositoryCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<IconSettingsRepositoryCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.IconSettingsCollectionName ?? "IconSettings";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<IconSettingsEntity>> Indices =>
    [
        new(Builders<IconSettingsEntity>.IndexKeys.Ascending(x => x.Key),
            new CreateIndexOptions { Unique = true, Name = "Key" })
    ];
}
