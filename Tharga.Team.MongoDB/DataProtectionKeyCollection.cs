using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>One Data Protection key, as the XML Data Protection writes it.</summary>
public record DataProtectionKeyEntity : EntityBase
{
    /// <summary>The name Data Protection gives the key.</summary>
    public required string FriendlyName { get; init; }

    /// <summary>The key element, as written.</summary>
    public required string Xml { get; init; }
}

public interface IDataProtectionKeyCollection : IDiskRepositoryCollection<DataProtectionKeyEntity>;

/// <summary>
/// The Data Protection key ring. Default collection name <c>DataProtectionKey</c>.
/// </summary>
/// <remarks>Shared by every instance that reads the same database, which is the point of keeping it here.</remarks>
public class DataProtectionKeyCollection : DiskRepositoryCollectionBase<DataProtectionKeyEntity>, IDataProtectionKeyCollection
{
    private readonly string _collectionName;

    public DataProtectionKeyCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<DataProtectionKeyCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.DataProtectionKeyCollectionName ?? "DataProtectionKey";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<DataProtectionKeyEntity>> Indices => [];
}
