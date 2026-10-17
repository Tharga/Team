using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>One user's stream of notices — one notice delivered at once, or the daily summary of one mail category — and when its next mail is due.</summary>
public record UserNoticeStreamEntity : EntityBase
{
    /// <summary>The user's key.</summary>
    public required string UserKey { get; init; }

    /// <summary>The stream.</summary>
    public required string Stream { get; init; }

    /// <summary>When the next mail is due while something waits, in UTC; far in the future while nothing does.</summary>
    public DateTime DueAt { get; init; }

    /// <summary>Raised by every notice added, so a release can tell one arrived while it counted what waits.</summary>
    public long Version { get; init; }

    /// <summary>Who holds it, while an instance does.</summary>
    public string ClaimedBy { get; init; }

    /// <summary>When the holder's lease runs out, in UTC.</summary>
    public DateTime? ClaimedUntil { get; init; }

    /// <summary>The key of the mail being queued, while it is.</summary>
    public string MailKey { get; init; }

    /// <summary>The notices that mail carries, while it is queued.</summary>
    public string[] MailItemIds { get; init; }
}

/// <summary>The collection of users' notice streams.</summary>
public interface IUserNoticeStreamCollection : IDiskRepositoryCollection<UserNoticeStreamEntity>;

/// <summary>Users' notice streams, one document per user and stream, default collection name <c>UserNoticeStream</c>.</summary>
/// <remarks>The unique index keeps one stream per user and name when two instances add to it at once; due streams are found by due time.</remarks>
public class UserNoticeStreamCollection : DiskRepositoryCollectionBase<UserNoticeStreamEntity>, IUserNoticeStreamCollection
{
    private readonly string _collectionName;

    /// <summary>Creates the collection under the configured name.</summary>
    public UserNoticeStreamCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserNoticeStreamCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserNoticeStreamCollectionName ?? ThargaTeamOptions.DefaultUserNoticeStreamCollectionName;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserNoticeStreamEntity>> Indices =>
    [
        new(Builders<UserNoticeStreamEntity>.IndexKeys.Ascending(x => x.UserKey).Ascending(x => x.Stream),
            new CreateIndexOptions { Unique = true, Name = "UserKey_Stream" }),
        new(Builders<UserNoticeStreamEntity>.IndexKeys.Ascending(x => x.DueAt),
            new CreateIndexOptions { Name = "DueAt" })
    ];
}
