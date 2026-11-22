using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>The platform's AI catalogue as stored: one document, so a change is atomic and the chat client reads it in one read.</summary>
public record PlatformAiCatalogueEntity : EntityBase
{
    /// <summary>Always <see cref="Singleton"/>: there is one catalogue, and the unique index on this keeps it so.</summary>
    public string Name { get; init; } = Singleton;

    public const string Singleton = "catalogue";

    public PlatformAiProviderEntity[] Providers { get; init; } = [];

    public PlatformAiModelEntity[] Models { get; init; } = [];

    [BsonIgnoreIfNull]
    public string DefaultModelId { get; init; }

    public DateTime ChangedAt { get; init; }

    public string ChangedByIdentity { get; init; }

    public string ChangedByName { get; init; }
}

public record PlatformAiProviderEntity
{
    public string Id { get; init; }

    public string Name { get; init; }

    [BsonRepresentation(BsonType.String)]
    public SupportAiConnectionKind Kind { get; init; }

    public string BaseAddress { get; init; }

    [BsonIgnoreIfNull]
    public string ProtectedApiKey { get; init; }
}

public record PlatformAiModelEntity
{
    public string Id { get; init; }

    public string ProviderId { get; init; }

    public string ModelId { get; init; }

    public string DisplayName { get; init; }

    [BsonIgnoreIfNull]
    public string ProcessedIn { get; init; }

    [BsonIgnoreIfNull]
    public string StoredIn { get; init; }

    [BsonIgnoreIfNull]
    public string AgreementUrl { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? PriceInput { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? PriceCachedInput { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? PriceCacheWrite { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? PriceOutput { get; init; }

    public string Currency { get; init; }

    public bool Offered { get; init; }
}

public interface IPlatformAiCatalogueCollection : IDiskRepositoryCollection<PlatformAiCatalogueEntity>;

public class PlatformAiCatalogueCollection : DiskRepositoryCollectionBase<PlatformAiCatalogueEntity>, IPlatformAiCatalogueCollection
{
    private readonly string _collectionName;

    public PlatformAiCatalogueCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<PlatformAiCatalogueCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.PlatformAiCatalogueCollectionName ?? "PlatformAiCatalogue";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<PlatformAiCatalogueEntity>> Indices =>
    [
        new(Builders<PlatformAiCatalogueEntity>.IndexKeys.Ascending(x => x.Name),
            new CreateIndexOptions { Unique = true, Name = "Name" })
    ];
}
