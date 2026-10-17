using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>The content one mail to many users shares, stored once, and once every mail of it is done what became of them.</summary>
public record UserMailBatchEntity : EntityBase
{
    /// <summary>The category's key.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>The sanitized HTML body, or null for a text-only mail.</summary>
    [BsonIgnoreIfNull]
    public string BodyHtml { get; init; }

    /// <summary>The plain-text body.</summary>
    public string Body { get; init; }

    /// <summary>The files; always an array, so their content can be removed in one update once every mail is done.</summary>
    public UserMailAttachmentEntity[] Attachments { get; init; } = [];

    /// <summary>How many mails were queued for it.</summary>
    public int RecipientCount { get; init; }

    /// <summary>When it was queued, in UTC.</summary>
    public DateTime QueuedAt { get; init; }

    /// <summary>When its last mail was sent, skipped or given up, in UTC; the retention period runs from here.</summary>
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; init; }

    /// <summary>What became of its mails, recorded when the last one was done.</summary>
    [BsonIgnoreIfNull]
    public UserMailBatchOutcomeEntity Outcome { get; init; }
}

/// <summary>What became of the mails of a batch, recorded once every one is done.</summary>
public record UserMailBatchOutcomeEntity
{
    /// <summary>How many were sent and not reported bounced; recounted when a bounce is reported after the outcome was recorded.</summary>
    public int Sent { get; init; }

    /// <summary>How many were skipped because the recipient does not receive the category.</summary>
    public int Skipped { get; init; }

    /// <summary>How many were given up.</summary>
    public int Failed { get; init; }

    /// <summary>How many the receiving server returned as undeliverable; zero on an outcome recorded before bounces were.</summary>
    public int Bounced { get; init; }

    /// <summary>The mails given up, each with why, at most a hundred.</summary>
    public UserMailFailureEntity[] Failures { get; init; } = [];
}

/// <summary>Why the mail to one recipient of a batch was given up.</summary>
public record UserMailFailureEntity
{
    /// <summary>The key of the user the mail was for.</summary>
    public required string UserKey { get; init; }

    /// <summary>Why it was given up.</summary>
    public string Reason { get; init; }
}

/// <summary>The collection of content shared by one mail to many users.</summary>
public interface IUserMailBatchCollection : IDiskRepositoryCollection<UserMailBatchEntity>;

/// <summary>Content shared by one mail to many users, default collection name <c>UserMailBatch</c>; a batch expires <see cref="ThargaTeamOptions.UserMailRetention"/> after its last mail is done.</summary>
public class UserMailBatchCollection : DiskRepositoryCollectionBase<UserMailBatchEntity>, IUserMailBatchCollection
{
    private readonly string _collectionName;
    private readonly TimeSpan _retention;

    /// <summary>Creates the collection under the configured name and retention.</summary>
    public UserMailBatchCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserMailBatchCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserMailBatchCollectionName ?? ThargaTeamOptions.DefaultUserMailBatchCollectionName;
        _retention = options?.Value.UserMailRetention ?? ThargaTeamOptions.DefaultUserMailRetention;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserMailBatchEntity>> Indices =>
    [
        new(Builders<UserMailBatchEntity>.IndexKeys.Ascending(x => x.CompletedAt),
            new CreateIndexOptions { ExpireAfter = _retention, Name = "CompletedAt_ttl" })
    ];
}
