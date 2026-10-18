using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>A message staff sent to contacts, as the log records it, with every distinct address it was listed for.</summary>
public record ContactMessageEntity : EntityBase
{
    /// <summary>The message's id, which is the id of the batch its mails were queued in; unique.</summary>
    public required string MessageId { get; init; }

    /// <summary>The contact category's key.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>How many files it carried.</summary>
    public int AttachmentCount { get; init; }

    /// <summary>The key of the user who sent it, or null.</summary>
    [BsonIgnoreIfNull]
    public string SentByUserKey { get; init; }

    /// <summary>The name of whoever sent it.</summary>
    [BsonIgnoreIfNull]
    public string SentByName { get; init; }

    /// <summary>When it was sent, in UTC; the retention period runs from here.</summary>
    public DateTime SentAt { get; init; }

    /// <summary>How many addresses one mail was queued to.</summary>
    public int RecipientCount { get; init; }

    /// <summary>How many listed addresses had opted out.</summary>
    public int OptedOutCount { get; init; }

    /// <summary>How many listed addresses were left out.</summary>
    public int LeftOutCount { get; init; }

    /// <summary>Every distinct address listed and how it was checked when sent.</summary>
    public ContactMessageAddresseeEntity[] Addressees { get; init; } = [];
}

/// <summary>One distinct address of a message to contacts, as it was checked when the message was sent.</summary>
public record ContactMessageAddresseeEntity
{
    /// <summary>The address.</summary>
    public required string Address { get; init; }

    /// <summary>The name it was addressed to.</summary>
    [BsonIgnoreIfNull]
    public string Name { get; init; }

    /// <summary>The host's detail line.</summary>
    [BsonIgnoreIfNull]
    public string Detail { get; init; }

    /// <summary>The host's references of the rows it stands for.</summary>
    public string[] References { get; init; } = [];

    /// <summary>Whether it was mailed, stored by name.</summary>
    [BsonRepresentation(BsonType.String)]
    public ContactAddresseeStatus Status { get; init; }

    /// <summary>Whether it had opted out of all contact mail.</summary>
    [BsonIgnoreIfDefault]
    public bool OptedOutOfAll { get; init; }

    /// <summary>Whether it was left out as no mail address.</summary>
    [BsonIgnoreIfDefault]
    public bool InvalidAddress { get; init; }

    /// <summary>The host's reason for leaving it out.</summary>
    [BsonIgnoreIfNull]
    public string ExcludedReason { get; init; }
}

/// <summary>The collection of the log of messages to contacts.</summary>
public interface IContactMessageCollection : IDiskRepositoryCollection<ContactMessageEntity>;

/// <summary>The log of messages staff sent to contacts, default collection name <c>ContactMessage</c>; a message expires <see cref="ThargaTeamOptions.UserMailRetention"/> after it was sent, with the mails it queued.</summary>
public class ContactMessageCollection : DiskRepositoryCollectionBase<ContactMessageEntity>, IContactMessageCollection
{
    private readonly string _collectionName;
    private readonly TimeSpan _retention;

    /// <summary>Creates the collection under the configured name and retention.</summary>
    public ContactMessageCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<ContactMessageCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.ContactMessageCollectionName ?? ThargaTeamOptions.DefaultContactMessageCollectionName;
        _retention = options?.Value.UserMailRetention ?? ThargaTeamOptions.DefaultUserMailRetention;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<ContactMessageEntity>> Indices =>
    [
        new(Builders<ContactMessageEntity>.IndexKeys.Ascending(x => x.MessageId),
            new CreateIndexOptions { Unique = true, Name = "MessageId" }),
        new(Builders<ContactMessageEntity>.IndexKeys.Descending(x => x.SentAt).Ascending(x => x.MessageId),
            new CreateIndexOptions { Name = "SentAt_MessageId" }),
        new(Builders<ContactMessageEntity>.IndexKeys.Ascending(x => x.SentAt),
            new CreateIndexOptions { ExpireAfter = _retention, Name = "SentAt_ttl" })
    ];
}
