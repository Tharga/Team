using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>One queued mail to a user and where it stands.</summary>
public record UserMailEntity : EntityBase
{
    /// <summary>The key of the user the mail is for.</summary>
    public required string RecipientUserKey { get; init; }

    /// <summary>The category's key.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>The sanitized HTML body, or null for a text-only mail.</summary>
    [BsonIgnoreIfNull]
    public string BodyHtml { get; init; }

    /// <summary>The plain-text body.</summary>
    public string Body { get; init; }

    /// <summary>The files; always an array, so their content can be removed in one update once the mail is done.</summary>
    public UserMailAttachmentEntity[] Attachments { get; init; } = [];

    /// <summary>The reply address the mail was queued with, or null.</summary>
    [BsonIgnoreIfNull]
    public string ReplyTo { get; init; }

    /// <summary>The deduplication key, unique where present.</summary>
    [BsonIgnoreIfNull]
    public string DeduplicationKey { get; init; }

    /// <summary>Where the mail stands, stored by name.</summary>
    [BsonRepresentation(BsonType.String)]
    public UserMailStatus Status { get; init; }

    /// <summary>How many times it has been claimed for sending.</summary>
    public int Attempts { get; init; }

    /// <summary>When it was queued, in UTC.</summary>
    public DateTime QueuedAt { get; init; }

    /// <summary>When it is next due, in UTC.</summary>
    public DateTime NextAttemptAt { get; init; }

    /// <summary>Who holds it, while a sender does.</summary>
    [BsonIgnoreIfNull]
    public string ClaimedBy { get; init; }

    /// <summary>When the holder's lease runs out, in UTC.</summary>
    [BsonIgnoreIfNull]
    public DateTime? ClaimedUntil { get; init; }

    /// <summary>When it was sent, skipped or given up, in UTC; the retention period runs from here.</summary>
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; init; }

    /// <summary>Why it was skipped or failed, or why the last attempt did not succeed.</summary>
    [BsonIgnoreIfNull]
    public string Reason { get; init; }

    /// <summary>The <c>Message-ID</c> it was sent with.</summary>
    [BsonIgnoreIfNull]
    public string MessageId { get; init; }
}

/// <summary>A file kept with a queued mail; its content is removed once the mail is done.</summary>
public record UserMailAttachmentEntity
{
    /// <summary>The name the recipient sees.</summary>
    public required string FileName { get; init; }

    /// <summary>The declared content type.</summary>
    public required string ContentType { get; init; }

    /// <summary>The bytes, until the mail is done.</summary>
    [BsonIgnoreIfNull]
    public byte[] Content { get; init; }

    /// <summary>The size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>The Content-ID an HTML body names it by, or null.</summary>
    [BsonIgnoreIfNull]
    public string ContentId { get; init; }
}

/// <summary>The collection of queued mail to users.</summary>
public interface IUserMailCollection : IDiskRepositoryCollection<UserMailEntity>;

/// <summary>Queued mail to users, default collection name <c>UserMail</c>.</summary>
/// <remarks>
/// Due mail is found by status and due time; a deduplication key is unique where one was given; and a mail that is
/// done expires after <see cref="ThargaTeamOptions.UserMailRetention"/>, while a queued one never does.
/// </remarks>
public class UserMailCollection : DiskRepositoryCollectionBase<UserMailEntity>, IUserMailCollection
{
    private readonly string _collectionName;
    private readonly TimeSpan _retention;

    /// <summary>Creates the collection under the configured name and retention.</summary>
    public UserMailCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserMailCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserMailCollectionName ?? ThargaTeamOptions.DefaultUserMailCollectionName;
        _retention = options?.Value.UserMailRetention ?? ThargaTeamOptions.DefaultUserMailRetention;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserMailEntity>> Indices =>
    [
        new(Builders<UserMailEntity>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.NextAttemptAt),
            new CreateIndexOptions { Name = "Status_NextAttemptAt" }),
        new(Builders<UserMailEntity>.IndexKeys.Ascending(x => x.DeduplicationKey),
            new CreateIndexOptions { Unique = true, Sparse = true, Name = "DeduplicationKey" }),
        new(Builders<UserMailEntity>.IndexKeys.Ascending(x => x.CompletedAt),
            new CreateIndexOptions { ExpireAfter = _retention, Name = "CompletedAt_ttl" })
    ];
}
