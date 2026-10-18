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
    /// <summary>The key of the user the mail is for; null for a mail queued to an address alone.</summary>
    public string RecipientUserKey { get; init; }

    /// <summary>The address the mail is or was addressed to: the one it was queued with, or for a mail to a user the one on their record when it was sent, in lower case.</summary>
    [BsonIgnoreIfNull]
    public string RecipientAddress { get; init; }

    /// <summary>The name a mail queued to an address alone is addressed to.</summary>
    [BsonIgnoreIfNull]
    public string RecipientName { get; init; }

    /// <summary>The category's key.</summary>
    public required string Category { get; init; }

    /// <summary>Which kind of mail it is, stored by name; null on a mail stored before kinds were, read as a message when it names a batch and as a mail otherwise.</summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public UserMailKind? Kind { get; init; }

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

    /// <summary>The batch whose content the mail carries, or null for a mail queued on its own.</summary>
    [BsonIgnoreIfNull]
    public string BatchId { get; init; }

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

    /// <summary>Why it was skipped, failed or bounced, or why the last attempt did not succeed.</summary>
    [BsonIgnoreIfNull]
    public string Reason { get; init; }

    /// <summary>The <c>Message-ID</c> it was sent with, which a bounce report names it by.</summary>
    [BsonIgnoreIfNull]
    public string MessageId { get; init; }

    /// <summary>When the report that it bounced was read, in UTC.</summary>
    [BsonIgnoreIfNull]
    public DateTime? BouncedAt { get; init; }
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
public interface IUserMailCollection : IDiskRepositoryCollection<UserMailEntity>
{
    /// <summary>For each category, the mails sent or bounced to a user since <paramref name="since"/>, and to how many users, in one aggregation on the server.</summary>
    Task<IReadOnlyList<UserMailCategorySent>> CountSentByCategoryAsync(DateTime since, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(CountSentByCategoryAsync)}.");

    /// <summary>How long a mail is kept once it is done.</summary>
    TimeSpan Retention => ThargaTeamOptions.DefaultUserMailRetention;
}

/// <summary>Queued mail to users, default collection name <c>UserMail</c>.</summary>
/// <remarks>
/// Due mail is found by status and due time; a deduplication key is unique where one was given; a batch's mail by its
/// batch and status; a user's history by their key or address, newest first; a sent mail by the <c>Message-ID</c> a bounce
/// report names; how often each category was sent by status, completion, category and recipient, read from the index
/// alone; and a mail that is done expires after <see cref="ThargaTeamOptions.UserMailRetention"/>, while a queued one never
/// does.
/// </remarks>
public class UserMailCollection : DiskRepositoryCollectionBase<UserMailEntity>, IUserMailCollection
{
    private static readonly FilterDefinitionBuilder<UserMailEntity> Filter = Builders<UserMailEntity>.Filter;

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
            new CreateIndexOptions { ExpireAfter = _retention, Name = "CompletedAt_ttl" }),
        new(Builders<UserMailEntity>.IndexKeys.Ascending(x => x.BatchId).Ascending(x => x.Status),
            new CreateIndexOptions { Name = "BatchId_Status" }),
        new(Builders<UserMailEntity>.IndexKeys.Ascending(x => x.RecipientUserKey).Descending(x => x.Id),
            new CreateIndexOptions { Name = "RecipientUserKey_Id" }),
        new(Builders<UserMailEntity>.IndexKeys.Ascending(x => x.RecipientAddress).Descending(x => x.Id),
            new CreateIndexOptions { Name = "RecipientAddress_Id" }),
        new(Builders<UserMailEntity>.IndexKeys.Ascending(x => x.MessageId),
            new CreateIndexOptions { Sparse = true, Name = "MessageId" }),
        new(Builders<UserMailEntity>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.CompletedAt).Ascending(x => x.Category).Ascending(x => x.RecipientUserKey),
            new CreateIndexOptions { Name = "Status_CompletedAt_Category_RecipientUserKey" })
    ];

    /// <inheritdoc />
    public TimeSpan Retention => _retention;

    /// <inheritdoc />
    public Task<IReadOnlyList<UserMailCategorySent>> CountSentByCategoryAsync(DateTime since, CancellationToken cancellationToken = default)
        => ExecuteAsync(nameof(CountSentByCategoryAsync), async (collection, _, ct) =>
        {
            var rows = await collection.Aggregate(SentByCategory(since), cancellationToken: ct).ToListAsync(ct);
            IReadOnlyList<UserMailCategorySent> counted = [.. rows.Select(ToCategorySent)];
            return (counted, counted.Count);
        }, Operation.Read, cancellationToken, SentSince(since));

    /// <summary>Mail sent or bounced, completed at or after <paramref name="since"/>; the fields the statistics index leads with, so nothing else is read.</summary>
    internal static FilterDefinition<UserMailEntity> SentSince(DateTime since)
        => Filter.In(x => x.Status, [UserMailStatus.Sent, UserMailStatus.Bounced]) & Filter.Gte(x => x.CompletedAt, since);

    /// <summary>
    /// The mails sent since a time grouped by category and recipient, mail to an address alone left out, then by category:
    /// how many mails and how many recipients.
    /// </summary>
    /// <remarks>Every field it reads is in <c>Status_CompletedAt_Category_RecipientUserKey</c>, so the server answers from the index without reading a mail.</remarks>
    internal static PipelineDefinition<UserMailEntity, BsonDocument> SentByCategory(DateTime since)
    {
        IPipelineStageDefinition[] stages =
        [
            PipelineStageDefinitionBuilder.Match(SentSince(since)),
            new BsonDocumentPipelineStageDefinition<UserMailEntity, BsonDocument>(new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "c", "$" + nameof(UserMailEntity.Category) }, { "u", "$" + nameof(UserMailEntity.RecipientUserKey) } } },
                { "n", new BsonDocument("$sum", 1) }
            })),
            new BsonDocumentPipelineStageDefinition<BsonDocument, BsonDocument>(new BsonDocument("$match", new BsonDocument("_id.u", new BsonDocument("$ne", BsonNull.Value)))),
            new BsonDocumentPipelineStageDefinition<BsonDocument, BsonDocument>(new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$_id.c" },
                { "sent", new BsonDocument("$sum", "$n") },
                { "recipients", new BsonDocument("$sum", 1) }
            }))
        ];

        return new PipelineStagePipelineDefinition<UserMailEntity, BsonDocument>(stages);
    }

    /// <summary>One row of <see cref="SentByCategory"/>.</summary>
    internal static UserMailCategorySent ToCategorySent(BsonDocument row)
        => new(row["_id"].IsString ? row["_id"].AsString : null, row["sent"].ToInt32(), row["recipients"].ToInt32());
}
