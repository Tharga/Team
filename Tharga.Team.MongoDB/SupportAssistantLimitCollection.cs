using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>A team's own limit for the support assistant. The document existing is what makes it the team's own, so a missing limit means none.</summary>
public record SupportAssistantLimitEntity : EntityBase
{
    /// <summary>The team; one document per team.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The limit per period in the catalogue's currency; absent means the team has no limit.</summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? Limit { get; init; }

    public DateTime ChangedAt { get; init; }

    public string ChangedByIdentity { get; init; }

    public string ChangedByName { get; init; }
}

public interface ISupportAssistantLimitCollection : IDiskRepositoryCollection<SupportAssistantLimitEntity>;

/// <summary>
/// Each team's own assistant limit. Default collection name <c>SupportAssistantLimit</c>.
/// </summary>
/// <remarks>The unique index on <see cref="SupportAssistantLimitEntity.TeamKey"/> keeps it to one limit per team when two staff save at once.</remarks>
public class SupportAssistantLimitCollection : DiskRepositoryCollectionBase<SupportAssistantLimitEntity>, ISupportAssistantLimitCollection
{
    private readonly string _collectionName;

    public SupportAssistantLimitCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<SupportAssistantLimitCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.SupportAssistantLimitCollectionName ?? "SupportAssistantLimit";
    }

    public override string CollectionName => _collectionName;

    public override IEnumerable<CreateIndexModel<SupportAssistantLimitEntity>> Indices =>
    [
        new(Builders<SupportAssistantLimitEntity>.IndexKeys.Ascending(x => x.TeamKey),
            new CreateIndexOptions { Unique = true, Name = "TeamKey" })
    ];
}
