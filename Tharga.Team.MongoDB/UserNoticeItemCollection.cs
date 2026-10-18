using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>One occurrence as it reaches one user, waiting in their stream until it is mailed.</summary>
public record UserNoticeItemEntity : EntityBase
{
    /// <summary>The id of the occurrence it came from.</summary>
    public required string NoticeId { get; init; }

    /// <summary>What identifies the occurrence; unique together with the user.</summary>
    public required string OccurrenceKey { get; init; }

    /// <summary>The key of the user it reaches.</summary>
    public required string UserKey { get; init; }

    /// <summary>The team the occurrence belongs to.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The notice's key.</summary>
    public required string NoticeKey { get; init; }

    /// <summary>The mail category it is sent in.</summary>
    public required string Category { get; init; }

    /// <summary>The user's stream it waits in.</summary>
    public required string Stream { get; init; }

    /// <summary>How it is delivered, stored by name.</summary>
    [BsonRepresentation(BsonType.String)]
    public UserNoticeDelivery Delivery { get; init; }

    /// <summary>The id of the thing it is about.</summary>
    public required string SubjectId { get; init; }

    /// <summary>How the thing is named to a reader, or null.</summary>
    [BsonIgnoreIfNull]
    public string SubjectText { get; init; }

    /// <summary>The placeholder values, by name.</summary>
    public Dictionary<string, string> Values { get; init; } = [];

    /// <summary>The site path of the thing, or null.</summary>
    [BsonIgnoreIfNull]
    public string LinkPath { get; init; }

    /// <summary>When the occurrence was raised, in UTC.</summary>
    public DateTime RaisedAt { get; init; }

    /// <summary>Where it stands, stored by name.</summary>
    [BsonRepresentation(BsonType.String)]
    public UserNoticeItemStatus Status { get; init; }

    /// <summary>The id of the queued mail that carries it.</summary>
    [BsonIgnoreIfNull]
    public string MailId { get; init; }

    /// <summary>When it was mailed or given up, in UTC; the retention period runs from here.</summary>
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; init; }

    /// <summary>Why it was given up.</summary>
    [BsonIgnoreIfNull]
    public string Reason { get; init; }
}

/// <summary>The collection of users' notices.</summary>
public interface IUserNoticeItemCollection : IDiskRepositoryCollection<UserNoticeItemEntity>;

/// <summary>Users' notices, default collection name <c>UserNoticeItem</c>.</summary>
/// <remarks>
/// The unique index on occurrence and user is what keeps an occurrence from reaching anyone twice, however often and
/// wherever it is raised; it lasts as long as the notice is kept, <see cref="ThargaTeamOptions.UserNoticeRetention"/>
/// after it was mailed. What waits is found by user, stream and status.
/// </remarks>
public class UserNoticeItemCollection : DiskRepositoryCollectionBase<UserNoticeItemEntity>, IUserNoticeItemCollection
{
    private readonly string _collectionName;
    private readonly TimeSpan _retention;

    /// <summary>Creates the collection under the configured name and retention.</summary>
    public UserNoticeItemCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserNoticeItemCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserNoticeItemCollectionName ?? ThargaTeamOptions.DefaultUserNoticeItemCollectionName;
        _retention = options?.Value.UserNoticeRetention ?? ThargaTeamOptions.DefaultUserNoticeRetention;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserNoticeItemEntity>> Indices =>
    [
        new(Builders<UserNoticeItemEntity>.IndexKeys.Ascending(x => x.OccurrenceKey).Ascending(x => x.UserKey),
            new CreateIndexOptions { Unique = true, Name = "OccurrenceKey_UserKey" }),
        new(Builders<UserNoticeItemEntity>.IndexKeys.Ascending(x => x.UserKey).Ascending(x => x.Stream).Ascending(x => x.Status).Ascending(x => x.RaisedAt),
            new CreateIndexOptions { Name = "UserKey_Stream_Status_RaisedAt" }),
        new(Builders<UserNoticeItemEntity>.IndexKeys.Ascending(x => x.CompletedAt),
            new CreateIndexOptions { ExpireAfter = _retention, Name = "CompletedAt_ttl" })
    ];
}
