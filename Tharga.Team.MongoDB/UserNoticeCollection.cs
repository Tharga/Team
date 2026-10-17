using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>A raised notice occurrence and where its distribution stands.</summary>
public record UserNoticeEntity : EntityBase
{
    /// <summary>The notice's key.</summary>
    public required string NoticeKey { get; init; }

    /// <summary>The team the occurrence belongs to.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The id of the thing it is about.</summary>
    public required string SubjectId { get; init; }

    /// <summary>How the thing is named to a reader, or null.</summary>
    [BsonIgnoreIfNull]
    public string SubjectText { get; init; }

    /// <summary>What tells occurrences about the same thing apart, or null.</summary>
    [BsonIgnoreIfNull]
    public string Discriminator { get; init; }

    /// <summary>The placeholder values, by name.</summary>
    public Dictionary<string, string> Values { get; init; } = [];

    /// <summary>The site path of the thing, or null.</summary>
    [BsonIgnoreIfNull]
    public string LinkPath { get; init; }

    /// <summary>The users the raiser named.</summary>
    public string[] Recipients { get; init; } = [];

    /// <summary>When it was raised, in UTC.</summary>
    public DateTime RaisedAt { get; init; }

    /// <summary>Where its distribution stands, stored by name.</summary>
    [BsonRepresentation(BsonType.String)]
    public UserNoticeStatus Status { get; init; }

    /// <summary>How many times it has been claimed.</summary>
    public int Attempts { get; init; }

    /// <summary>When it is next due, in UTC.</summary>
    public DateTime NextAttemptAt { get; init; }

    /// <summary>Who holds it, while an instance does.</summary>
    [BsonIgnoreIfNull]
    public string ClaimedBy { get; init; }

    /// <summary>When the holder's lease runs out, in UTC.</summary>
    [BsonIgnoreIfNull]
    public DateTime? ClaimedUntil { get; init; }

    /// <summary>When it was distributed or given up, in UTC; the retention period runs from here.</summary>
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; init; }

    /// <summary>How many users it was given to.</summary>
    public int RecipientCount { get; init; }

    /// <summary>Why it was given up, or why the last attempt did not succeed.</summary>
    [BsonIgnoreIfNull]
    public string Reason { get; init; }
}

/// <summary>The collection of raised notice occurrences.</summary>
public interface IUserNoticeCollection : IDiskRepositoryCollection<UserNoticeEntity>;

/// <summary>Raised notice occurrences, default collection name <c>UserNotice</c>.</summary>
/// <remarks>Due occurrences are found by status and due time, and one that is done expires after <see cref="ThargaTeamOptions.UserNoticeRetention"/>.</remarks>
public class UserNoticeCollection : DiskRepositoryCollectionBase<UserNoticeEntity>, IUserNoticeCollection
{
    private readonly string _collectionName;
    private readonly TimeSpan _retention;

    /// <summary>Creates the collection under the configured name and retention.</summary>
    public UserNoticeCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserNoticeCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserNoticeCollectionName ?? ThargaTeamOptions.DefaultUserNoticeCollectionName;
        _retention = options?.Value.UserNoticeRetention ?? ThargaTeamOptions.DefaultUserNoticeRetention;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserNoticeEntity>> Indices =>
    [
        new(Builders<UserNoticeEntity>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.NextAttemptAt),
            new CreateIndexOptions { Name = "Status_NextAttemptAt" }),
        new(Builders<UserNoticeEntity>.IndexKeys.Ascending(x => x.CompletedAt),
            new CreateIndexOptions { ExpireAfter = _retention, Name = "CompletedAt_ttl" })
    ];
}
