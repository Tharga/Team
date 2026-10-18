using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>An address that opted out of contact mail, of one category or of all of it.</summary>
public record ContactMailSuppressionEntity : EntityBase
{
    /// <summary>The address, trimmed and in lower case.</summary>
    public required string Address { get; init; }

    /// <summary>The category's key, or <c>*</c> for every contact mail.</summary>
    public required string Category { get; init; }

    /// <summary>When it was asked for, in UTC.</summary>
    public required DateTime SuppressedAt { get; init; }

    /// <summary>How it was asked for, stored by name.</summary>
    [BsonRepresentation(BsonType.String)]
    public required ContactMailSuppressionSource Source { get; init; }
}

/// <summary>The collection of addresses that opted out of contact mail.</summary>
public interface IContactMailSuppressionCollection : IDiskRepositoryCollection<ContactMailSuppressionEntity>;

/// <summary>Addresses that opted out of contact mail, one document per address and category, default collection name <c>ContactMailSuppression</c>; kept for good.</summary>
/// <remarks>The unique index is what keeps one opt-out per address and category when two requests race, and what reads an address's opt-outs.</remarks>
public class ContactMailSuppressionCollection : DiskRepositoryCollectionBase<ContactMailSuppressionEntity>, IContactMailSuppressionCollection
{
    private readonly string _collectionName;

    /// <summary>Creates the collection under the configured name.</summary>
    public ContactMailSuppressionCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ContactMailSuppressionCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.ContactMailSuppressionCollectionName ?? ThargaTeamOptions.DefaultContactMailSuppressionCollectionName;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<ContactMailSuppressionEntity>> Indices =>
    [
        new(Builders<ContactMailSuppressionEntity>.IndexKeys.Ascending(x => x.Address).Ascending(x => x.Category),
            new CreateIndexOptions { Unique = true, Name = "Address_Category" })
    ];
}
