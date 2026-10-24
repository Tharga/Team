using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>A team's AI connection, with the key only as Data Protection protected it.</summary>
public record SupportAiConnectionEntity : EntityBase
{
    /// <summary>The team; one document per team.</summary>
    public required string TeamKey { get; init; }

    /// <summary>Stored by name, so a reordered enum cannot turn one team's choice into another.</summary>
    [BsonRepresentation(BsonType.String)]
    public SupportAiConnectionKind Kind { get; init; }

    public string BaseAddress { get; init; }

    public string Model { get; init; }

    /// <summary>The key as Data Protection protected it. Never the key itself.</summary>
    public string ProtectedApiKey { get; init; }

    public DateTime ChangedAt { get; init; }

    public string ChangedByIdentity { get; init; }

    public string ChangedByName { get; init; }
}

public interface ISupportAiConnectionCollection : IDiskRepositoryCollection<SupportAiConnectionEntity>;

/// <summary>
/// Each team's AI connection. Default collection name <c>SupportAiConnection</c>.
/// </summary>
/// <remarks>The unique index on <see cref="SupportAiConnectionEntity.TeamKey"/> is what keeps it to one connection per team when two administrators save at once.</remarks>
public class SupportAiConnectionCollection : DiskRepositoryCollectionBase<SupportAiConnectionEntity>, ISupportAiConnectionCollection
{
    private readonly string _collectionName;

    public SupportAiConnectionCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SupportAiConnectionCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.SupportAiConnectionCollectionName ?? "SupportAiConnection";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<SupportAiConnectionEntity>> Indices =>
    [
        new(Builders<SupportAiConnectionEntity>.IndexKeys.Ascending(x => x.TeamKey),
            new CreateIndexOptions { Unique = true, Name = "TeamKey" })
    ];
}
