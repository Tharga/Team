using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>A message staff sent to users, as the sent log records it.</summary>
public record UserMessageEntity : EntityBase
{
    /// <summary>The message's id, which is the id of the batch its mails were queued in; unique.</summary>
    public required string MessageId { get; init; }

    /// <summary>The selection as it was chosen.</summary>
    public required UserMessageRecipientsEntity Recipients { get; init; }

    /// <summary>The category's key.</summary>
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

    /// <summary>How many users it reached when it was sent.</summary>
    public int RecipientCount { get; init; }
}

/// <summary>A message's selection as it was chosen.</summary>
public record UserMessageRecipientsEntity
{
    /// <summary>The teams it applied within.</summary>
    public string[] TeamKeys { get; init; } = [];

    /// <summary>Whether it applied within every team.</summary>
    public bool AllTeams { get; init; }

    /// <summary>Users chosen by key.</summary>
    public string[] UserKeys { get; init; } = [];

    /// <summary>Tenant roles chosen.</summary>
    public string[] Roles { get; init; } = [];

    /// <summary>Access levels chosen, stored by name.</summary>
    public string[] AccessLevels { get; init; } = [];

    /// <summary>Whether everyone in the teams was chosen.</summary>
    public bool EveryMember { get; init; }
}

/// <summary>The collection of the sent log.</summary>
public interface IUserMessageCollection : IDiskRepositoryCollection<UserMessageEntity>;

/// <summary>The log of messages staff sent to users, default collection name <c>UserMessage</c>; a message expires <see cref="ThargaTeamOptions.UserMailRetention"/> after it was sent.</summary>
public class UserMessageCollection : DiskRepositoryCollectionBase<UserMessageEntity>, IUserMessageCollection
{
    private readonly string _collectionName;
    private readonly TimeSpan _retention;

    /// <summary>Creates the collection under the configured name and retention.</summary>
    public UserMessageCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserMessageCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserMessageCollectionName ?? ThargaTeamOptions.DefaultUserMessageCollectionName;
        _retention = options?.Value.UserMailRetention ?? ThargaTeamOptions.DefaultUserMailRetention;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserMessageEntity>> Indices =>
    [
        new(Builders<UserMessageEntity>.IndexKeys.Ascending(x => x.MessageId),
            new CreateIndexOptions { Unique = true, Name = "MessageId" }),
        new(Builders<UserMessageEntity>.IndexKeys.Descending(x => x.SentAt).Ascending(x => x.MessageId),
            new CreateIndexOptions { Name = "SentAt_MessageId" }),
        new(Builders<UserMessageEntity>.IndexKeys.Ascending(x => x.SentAt),
            new CreateIndexOptions { ExpireAfter = _retention, Name = "SentAt_ttl" })
    ];
}
