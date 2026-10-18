using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserMailOutbox"/>.</summary>
/// <remarks>
/// <b>Every conditional write is one find-and-modify on the server</b>, which is why each passes
/// <see cref="EMode.FirstOrDefault"/>: it is the only mode that sends the filter as the update's own, so "due and
/// nobody holds it" and "this claimant still holds it" are decided atomically rather than read and then written.
/// <para>
/// <b>A batch is stored once and its mails name it.</b> When the last of them is done, the outcome is recorded on
/// the batch and its files' bytes are removed; whoever records a mail done checks for that, and so does a read, so
/// a writer that stopped in between is made good the next time the batch is read.
/// </para>
/// </remarks>
internal sealed class MongoUserMailOutbox(IUserMailCollection collection, IUserMailBatchCollection batches) : IUserMailOutbox
{
    private const string AttachmentContentPath = "Attachments.$[].Content";
    private const string AttachmentContentField = "Attachments.Content";

    private static readonly FilterDefinitionBuilder<UserMailEntity> Filter = Builders<UserMailEntity>.Filter;

    public async Task<UserMailQueued> AddAsync(UserMail mail, DateTime queuedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);

        if (mail.DeduplicationKey != null && await FindByDeduplicationKeyAsync(mail.DeduplicationKey, cancellationToken) is { } existing)
            return new UserMailQueued(existing.Id.ToString(), true);

        var entity = ToEntity(mail, queuedAt);

        try
        {
            await collection.AddAsync(entity);
            return new UserMailQueued(entity.Id.ToString(), false);
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey && mail.DeduplicationKey != null)
        {
            var winner = await FindByDeduplicationKeyAsync(mail.DeduplicationKey, cancellationToken);
            if (winner == null) throw;

            return new UserMailQueued(winner.Id.ToString(), true);
        }
    }

    /// <remarks>Inserted already held, so no sender on any instance can claim it between the insert and the caller's attempt.</remarks>
    public async Task<UserMailItem> AddClaimedAsync(UserMail mail, DateTime queuedAt, string claimant, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimant);
        if (mail.DeduplicationKey != null) throw new ArgumentException("A mail sent at once takes no deduplication key.", nameof(mail));

        var entity = ToEntity(mail, queuedAt) with { ClaimedBy = claimant, ClaimedUntil = queuedAt + lease, Attempts = 1 };
        await collection.AddAsync(entity);

        return ToItem(entity);
    }

    public async Task<UserMailItem> ClaimNextAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimant);

        var until = now + lease;
        var update = Builders<UserMailEntity>.Update
            .Set(x => x.ClaimedBy, claimant)
            .Set(x => x.ClaimedUntil, until)
            .Inc(x => x.Attempts, 1);

        var result = await collection.UpdateOneAsync(Claimable(now), update, new OneOption<UserMailEntity>
        {
            Mode = EMode.FirstOrDefault,
            Sort = Builders<UserMailEntity>.Sort.Ascending(x => x.NextAttemptAt)
        });

        var before = result?.Before;

        return before == null ? null : ToItem(before with { ClaimedBy = claimant, ClaimedUntil = until, Attempts = before.Attempts + 1 });
    }

    public Task<bool> MarkSentAsync(string id, string claimant, string messageId, string address, DateTime at, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, UserMailStatus.Sent, null, messageId, address, at);

    public Task<bool> MarkSkippedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, UserMailStatus.Skipped, reason, null, null, at);

    public Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, UserMailStatus.Failed, reason, null, null, at);

    public async Task<bool> PostponeAsync(string id, string claimant, string reason, DateTime nextAttemptAt, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return false;

        var update = Builders<UserMailEntity>.Update
            .Set(x => x.NextAttemptAt, nextAttemptAt)
            .Set(x => x.Reason, reason)
            .Unset(x => x.ClaimedBy)
            .Unset(x => x.ClaimedUntil);

        return (await collection.UpdateOneAsync(HeldBy(objectId, claimant), update, OneOption<UserMailEntity>.FirstOrDefault))?.Before != null;
    }

    public async Task<UserMailItem> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return null;

        var entity = await collection.GetOneAsync(x => x.Id == objectId, cancellationToken: cancellationToken);

        return entity == null ? null : ToItem(entity);
    }

    /// <remarks>The batch first, so a mail of it is never claimed before its content can be read.</remarks>
    public async Task<string> AddBatchAsync(UserMailBatch batch, DateTime queuedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var entity = ToBatchEntity(batch, queuedAt);
        var batchId = entity.Id.ToString();

        await batches.AddAsync(entity);
        await collection.AddManyAsync([.. ToBatchItems(batchId, batch, queuedAt)]);

        return batchId;
    }

    public async Task<UserMailBatch> GetBatchContentAsync(string batchId, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(batchId, out var objectId)) return null;

        var entity = await batches.GetOneAsync(x => x.Id == objectId, cancellationToken: cancellationToken);

        return entity == null ? null : ToBatch(entity);
    }

    public async Task<UserMailBatchDelivery> GetBatchDeliveryAsync(string batchId, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(batchId, out var objectId)) return null;

        var entity = await batches.GetOneAsync(x => x.Id == objectId, cancellationToken: cancellationToken);
        if (entity == null) return null;

        if (entity.Outcome != null) return Recorded(entity);

        var counted = await CountAsync(batchId, entity.RecipientCount, cancellationToken);
        if (!IsDone(counted, entity.RecipientCount)) return counted;

        await RecordOutcomeAsync(objectId, counted);
        return counted;
    }

    /// <remarks>
    /// The mail moves from sent to bounced in one conditional write, so of two reports only one changes it. A batch
    /// whose outcome is already recorded has its sent and bounced counts recounted and set rather than adjusted, so a
    /// report read while the outcome was being recorded leaves the counts true whichever write lands last.
    /// </remarks>
    public async Task<UserMailBounceResult> MarkBouncedAsync(string messageId, string reason, DateTime at, CancellationToken cancellationToken = default)
    {
        var forms = MessageIdForms(messageId);
        if (forms.Length == 0) return new UserMailBounceResult(UserMailBounceOutcome.Unknown);

        var update = Builders<UserMailEntity>.Update
            .Set(x => x.Status, UserMailStatus.Bounced)
            .Set(x => x.Reason, reason)
            .Set(x => x.BouncedAt, at);

        var before = (await collection.UpdateOneAsync(BouncedSent(forms), update, OneOption<UserMailEntity>.FirstOrDefault))?.Before;
        if (before != null)
        {
            if (before.BatchId != null) await RecountBouncesAsync(before.BatchId, cancellationToken);
            return new UserMailBounceResult(UserMailBounceOutcome.Bounced, before.Id.ToString());
        }

        var known = await collection.GetManyAsync(Filter.In(x => x.MessageId, forms), new Options<UserMailEntity> { Limit = 1 }, cancellationToken);
        var existing = (known?.Items ?? []).FirstOrDefault();

        return existing?.Status == UserMailStatus.Bounced
            ? new UserMailBounceResult(UserMailBounceOutcome.AlreadyBounced, existing.Id.ToString())
            : new UserMailBounceResult(UserMailBounceOutcome.Unknown);
    }

    /// <remarks>One aggregation answered from the collection's statistics index, so no mail is read.</remarks>
    public async Task<IReadOnlyList<UserMailCategorySent>> CountSentByCategoryAsync(DateTime since, CancellationToken cancellationToken = default)
        => await collection.CountSentByCategoryAsync(since, cancellationToken) ?? [];

    /// <remarks>One aggregation answered from the collection's contact statistics index, so no mail is read.</remarks>
    public async Task<IReadOnlyList<UserMailCategorySent>> CountSentToContactsByCategoryAsync(DateTime since, CancellationToken cancellationToken = default)
        => await collection.CountSentToContactsByCategoryAsync(since, cancellationToken) ?? [];

    public TimeSpan? Retention => collection.Retention;

    /// <summary>The sent mail with one of these <c>Message-ID</c> forms.</summary>
    internal static FilterDefinition<UserMailEntity> BouncedSent(string[] messageIds)
        => Filter.In(x => x.MessageId, messageIds) & Filter.Eq(x => x.Status, UserMailStatus.Sent);

    /// <summary>The <c>Message-ID</c> as it may have been recorded: bare, and in angle brackets; empty when there is none.</summary>
    internal static string[] MessageIdForms(string messageId)
    {
        var bare = messageId?.Trim().Trim('<', '>').Trim();

        return string.IsNullOrEmpty(bare) ? [] : [bare, $"<{bare}>"];
    }

    /// <summary>A recorded outcome's sent and bounced counts set from the batch's mails as they now stand.</summary>
    internal static UpdateDefinition<UserMailBatchEntity> SetBounceCounts(int sent, int bounced)
        => Builders<UserMailBatchEntity>.Update
            .Set(x => x.Outcome.Sent, sent)
            .Set(x => x.Outcome.Bounced, bounced);

    /// <summary>This batch, once its outcome is recorded.</summary>
    internal static FilterDefinition<UserMailBatchEntity> WithRecordedOutcome(ObjectId batchId)
        => Builders<UserMailBatchEntity>.Filter.Eq(x => x.Id, batchId) & Builders<UserMailBatchEntity>.Filter.Ne(x => x.Outcome, null);

    private async Task RecountBouncesAsync(string batchId, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(batchId, out var objectId)) return;

        var sent = (int)await collection.CountAsync(OfBatch(batchId, UserMailStatus.Sent), cancellationToken);
        var bounced = (int)await collection.CountAsync(OfBatch(batchId, UserMailStatus.Bounced), cancellationToken);

        await batches.UpdateOneAsync(WithRecordedOutcome(objectId), SetBounceCounts(sent, bounced), OneOption<UserMailBatchEntity>.FirstOrDefault);
    }

    public async Task<UserMailItemPage> ListBatchAsync(string batchId, UserMailStatus? status, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var filter = Filter.Eq(x => x.BatchId, batchId);
        if (status != null) filter &= Filter.Eq(x => x.Status, status.Value);
        if (cursor != null) filter &= Filter.Gt(x => x.Id, CursorId(cursor));

        return await PageAsync(filter, Builders<UserMailEntity>.Sort.Ascending(x => x.Id), pageSize, cancellationToken);
    }

    public async Task<UserMailItemPage> ListForRecipientAsync(string userKey, string address, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var filter = ForRecipient(userKey, address);
        if (cursor != null) filter &= Filter.Lt(x => x.Id, CursorId(cursor));

        return await PageAsync(filter, Builders<UserMailEntity>.Sort.Descending(x => x.Id), pageSize, cancellationToken);
    }

    /// <summary>The mails queued for this user's key, and those queued to this address with no user key other than contact mail; nothing when both are missing.</summary>
    internal static FilterDefinition<UserMailEntity> ForRecipient(string userKey, string address)
    {
        var parts = new List<FilterDefinition<UserMailEntity>>();
        if (!string.IsNullOrEmpty(userKey)) parts.Add(Filter.Eq(x => x.RecipientUserKey, userKey));
        if (!string.IsNullOrEmpty(address)) parts.Add(Filter.Eq(x => x.RecipientUserKey, null) & Filter.Eq(x => x.RecipientAddress, address) & Filter.Ne(x => x.Kind, UserMailKind.Contact));

        return parts.Count == 0 ? Filter.Eq(x => x.Id, ObjectId.Empty) : Filter.Or(parts);
    }

    /// <exception cref="ArgumentException">The cursor was not issued by this outbox.</exception>
    internal static ObjectId CursorId(string cursor)
        => ObjectId.TryParse(cursor, out var id) ? id : throw new ArgumentException($"'{cursor}' is not a cursor this outbox issued.", nameof(cursor));

    /// <remarks>The files' bytes are left on the server: a page describes mail, it does not resend it.</remarks>
    private async Task<UserMailItemPage> PageAsync(FilterDefinition<UserMailEntity> filter, SortDefinition<UserMailEntity> sort, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize, 1, 500);
        var found = await collection.GetManyAsync(filter, new Options<UserMailEntity>
        {
            Sort = sort,
            Limit = size + 1,
            Projection = Builders<UserMailEntity>.Projection.Exclude(AttachmentContentField)
        }, cancellationToken);

        var items = (found?.Items ?? []).ToArray();
        var page = items.Take(size).ToArray();

        return new UserMailItemPage
        {
            Items = [.. page.Select(ToItem)],
            NextCursor = items.Length > size ? page[^1].Id.ToString() : null
        };
    }

    /// <summary>Queued, due, and either never claimed or held by a lease that has run out.</summary>
    internal static FilterDefinition<UserMailEntity> Claimable(DateTime now)
        => Filter.Eq(x => x.Status, UserMailStatus.Queued)
           & Filter.Lte(x => x.NextAttemptAt, now)
           & (Filter.Eq(x => x.ClaimedUntil, null) | Filter.Lte(x => x.ClaimedUntil, now));

    /// <summary>This mail, still queued and still held by this claimant.</summary>
    internal static FilterDefinition<UserMailEntity> HeldBy(ObjectId id, string claimant)
        => Filter.Eq(x => x.Id, id) & Filter.Eq(x => x.Status, UserMailStatus.Queued) & Filter.Eq(x => x.ClaimedBy, claimant);

    /// <summary>The mails of this batch in this status.</summary>
    internal static FilterDefinition<UserMailEntity> OfBatch(string batchId, UserMailStatus status)
        => Filter.Eq(x => x.BatchId, batchId) & Filter.Eq(x => x.Status, status);

    /// <summary>The mails of this batch that are done, whichever way.</summary>
    internal static FilterDefinition<UserMailEntity> DoneOfBatch(string batchId)
        => Filter.Eq(x => x.BatchId, batchId) & Filter.Ne(x => x.Status, UserMailStatus.Queued);

    /// <summary>This batch, while its outcome is not yet recorded.</summary>
    internal static FilterDefinition<UserMailBatchEntity> Unrecorded(ObjectId batchId)
        => Builders<UserMailBatchEntity>.Filter.Eq(x => x.Id, batchId) & Builders<UserMailBatchEntity>.Filter.Eq(x => x.CompletedAt, null);

    /// <summary>The outcome recorded on the batch, its files' bytes removed.</summary>
    internal static UpdateDefinition<UserMailBatchEntity> RecordOutcome(UserMailBatchDelivery delivery)
        => Builders<UserMailBatchEntity>.Update
            .Set(x => x.CompletedAt, delivery.CompletedAt)
            .Set(x => x.Outcome, new UserMailBatchOutcomeEntity
            {
                Sent = delivery.Outcomes.Sent,
                Skipped = delivery.Outcomes.Skipped,
                Failed = delivery.Outcomes.Failed,
                Bounced = delivery.Outcomes.Bounced,
                Failures = [.. delivery.Failures.Select(x => new UserMailFailureEntity { UserKey = x.UserKey, Reason = x.Reason })]
            })
            .Unset(AttachmentContentPath);

    /// <summary>Whether every mail of a batch is accounted for and none waits.</summary>
    internal static bool IsDone(UserMailBatchDelivery delivery, int recipientCount)
        => delivery.Outcomes.Queued == 0 && delivery.Outcomes.Sent + delivery.Outcomes.Skipped + delivery.Outcomes.Failed + delivery.Outcomes.Bounced >= recipientCount;

    /// <remarks>The files' content goes with the outcome: the record of what was sent is kept, the bytes are not.</remarks>
    private async Task<bool> CompleteAsync(string id, string claimant, UserMailStatus status, string reason, string messageId, string address, DateTime at)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return false;

        var update = Builders<UserMailEntity>.Update
            .Set(x => x.Status, status)
            .Set(x => x.CompletedAt, at)
            .Unset(x => x.ClaimedBy)
            .Unset(x => x.ClaimedUntil)
            .Unset(AttachmentContentPath);

        update = reason == null ? update : update.Set(x => x.Reason, reason);
        update = messageId == null ? update : update.Set(x => x.MessageId, messageId);
        update = address == null ? update : update.Set(x => x.RecipientAddress, address);

        var before = (await collection.UpdateOneAsync(HeldBy(objectId, claimant), update, OneOption<UserMailEntity>.FirstOrDefault))?.Before;
        if (before == null) return false;

        if (before.BatchId != null && !await AnyQueuedAsync(before.BatchId)) await GetBatchDeliveryAsync(before.BatchId);

        return true;
    }

    /// <remarks>One mail is enough to know the batch is not done, so this reads at most one rather than counting them all after every mail.</remarks>
    private async Task<bool> AnyQueuedAsync(string batchId)
    {
        var queued = await collection.GetManyAsync(OfBatch(batchId, UserMailStatus.Queued), new Options<UserMailEntity> { Limit = 1 });

        return (queued?.Items ?? []).Any();
    }

    private async Task<UserMailBatchDelivery> CountAsync(string batchId, int recipientCount, CancellationToken cancellationToken)
    {
        var failed = await collection.GetManyAsync(OfBatch(batchId, UserMailStatus.Failed), new Options<UserMailEntity>
        {
            Sort = Builders<UserMailEntity>.Sort.Ascending(x => x.CompletedAt),
            Limit = UserMailBatchDelivery.MaxFailuresListed
        }, cancellationToken);

        var last = await collection.GetManyAsync(DoneOfBatch(batchId), new Options<UserMailEntity>
        {
            Sort = Builders<UserMailEntity>.Sort.Descending(x => x.CompletedAt),
            Limit = 1
        }, cancellationToken);

        return new UserMailBatchDelivery
        {
            Id = batchId,
            Recipients = recipientCount,
            Outcomes = new UserMailOutcomes
            {
                Queued = (int)await collection.CountAsync(OfBatch(batchId, UserMailStatus.Queued), cancellationToken),
                Sent = (int)await collection.CountAsync(OfBatch(batchId, UserMailStatus.Sent), cancellationToken),
                Skipped = (int)await collection.CountAsync(OfBatch(batchId, UserMailStatus.Skipped), cancellationToken),
                Failed = (int)await collection.CountAsync(OfBatch(batchId, UserMailStatus.Failed), cancellationToken),
                Bounced = (int)await collection.CountAsync(OfBatch(batchId, UserMailStatus.Bounced), cancellationToken)
            },
            Failures = [.. (failed?.Items ?? []).Select(x => new UserMailFailure { UserKey = x.RecipientUserKey, Reason = x.Reason ?? string.Empty })],
            CompletedAt = (last?.Items ?? []).FirstOrDefault()?.CompletedAt
        };
    }

    private Task RecordOutcomeAsync(ObjectId batchId, UserMailBatchDelivery delivery)
        => batches.UpdateOneAsync(Unrecorded(batchId), RecordOutcome(delivery), OneOption<UserMailBatchEntity>.FirstOrDefault);

    private Task<UserMailEntity> FindByDeduplicationKeyAsync(string key, CancellationToken cancellationToken)
        => collection.GetOneAsync(x => x.DeduplicationKey == key, cancellationToken: cancellationToken);

    internal static UserMailBatchDelivery Recorded(UserMailBatchEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Recipients = entity.RecipientCount,
        Outcomes = new UserMailOutcomes { Sent = entity.Outcome.Sent, Skipped = entity.Outcome.Skipped, Failed = entity.Outcome.Failed, Bounced = entity.Outcome.Bounced },
        Failures = [.. (entity.Outcome.Failures ?? []).Select(x => new UserMailFailure { UserKey = x.UserKey, Reason = x.Reason ?? string.Empty })],
        CompletedAt = entity.CompletedAt
    };

    internal static UserMailBatchEntity ToBatchEntity(UserMailBatch batch, DateTime queuedAt) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Category = batch.Category,
        Subject = batch.Subject,
        BodyHtml = batch.BodyHtml,
        Body = batch.Body,
        Attachments = [.. (batch.Attachments ?? []).Select(ToAttachmentEntity)],
        RecipientCount = (batch.RecipientUserKeys ?? []).Length + (batch.RecipientAddresses ?? []).Length,
        QueuedAt = queuedAt
    };

    /// <summary>One mail per user key the batch names, then one per address it names alone, in that order.</summary>
    internal static IEnumerable<UserMailEntity> ToBatchItems(string batchId, UserMailBatch batch, DateTime queuedAt)
        => (batch.RecipientUserKeys ?? []).Select(x => ToBatchItem(batchId, x, batch, queuedAt))
            .Concat((batch.RecipientAddresses ?? []).Where(x => x?.Address != null).Select(x => ToBatchItem(batchId, null, batch, queuedAt) with { RecipientAddress = x.Address, RecipientName = x.Name }));

    internal static UserMailEntity ToBatchItem(string batchId, string recipientUserKey, UserMailBatch batch, DateTime queuedAt) => new()
    {
        Id = ObjectId.GenerateNewId(),
        RecipientUserKey = recipientUserKey,
        Category = batch.Category,
        Subject = batch.Subject,
        Kind = batch.Kind,
        BatchId = batchId,
        Status = UserMailStatus.Queued,
        QueuedAt = queuedAt,
        NextAttemptAt = queuedAt
    };

    internal static UserMailBatch ToBatch(UserMailBatchEntity entity) => new()
    {
        RecipientUserKeys = [],
        Category = entity.Category,
        Subject = entity.Subject,
        BodyHtml = entity.BodyHtml,
        Body = entity.Body,
        Attachments = [.. (entity.Attachments ?? []).Select(ToAttachment)]
    };

    internal static UserMailEntity ToEntity(UserMail mail, DateTime queuedAt) => new()
    {
        Id = ObjectId.GenerateNewId(),
        RecipientUserKey = mail.RecipientUserKey,
        RecipientAddress = mail.RecipientUserKey == null ? mail.RecipientAddress : null,
        RecipientName = mail.RecipientUserKey == null ? mail.RecipientName : null,
        Category = mail.Category,
        Subject = mail.Subject,
        Kind = mail.Kind,
        BodyHtml = mail.BodyHtml,
        Body = mail.Body,
        Attachments = [.. (mail.Attachments ?? []).Select(ToAttachmentEntity)],
        ReplyTo = mail.ReplyTo,
        DeduplicationKey = mail.DeduplicationKey,
        Status = UserMailStatus.Queued,
        QueuedAt = queuedAt,
        NextAttemptAt = queuedAt
    };

    internal static UserMailItem ToItem(UserMailEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Mail = new UserMail
        {
            RecipientUserKey = entity.RecipientUserKey,
            RecipientAddress = entity.RecipientUserKey == null ? entity.RecipientAddress : null,
            RecipientName = entity.RecipientUserKey == null ? entity.RecipientName : null,
            Category = entity.Category,
            Subject = entity.Subject,
            BodyHtml = entity.BodyHtml,
            Body = entity.Body,
            Attachments = [.. (entity.Attachments ?? []).Select(ToAttachment)],
            ReplyTo = entity.ReplyTo,
            DeduplicationKey = entity.DeduplicationKey,
            Kind = entity.Kind ?? (entity.BatchId != null ? UserMailKind.Message : UserMailKind.Mail)
        },
        BatchId = entity.BatchId,
        RecipientAddress = entity.RecipientAddress,
        Status = entity.Status,
        Attempts = entity.Attempts,
        QueuedAt = entity.QueuedAt,
        NextAttemptAt = entity.NextAttemptAt,
        ClaimedBy = entity.ClaimedBy,
        ClaimedUntil = entity.ClaimedUntil,
        CompletedAt = entity.CompletedAt,
        Reason = entity.Reason,
        MessageId = entity.MessageId,
        BouncedAt = entity.BouncedAt
    };

    private static UserMailAttachmentEntity ToAttachmentEntity(UserMailAttachment x) => new()
    {
        FileName = x.FileName,
        ContentType = x.ContentType,
        Content = x.Content,
        Size = x.Content?.LongLength ?? x.Size,
        ContentId = x.ContentId
    };

    private static UserMailAttachment ToAttachment(UserMailAttachmentEntity x) => new()
    {
        FileName = x.FileName,
        ContentType = x.ContentType,
        Content = x.Content,
        Size = x.Size,
        ContentId = x.ContentId
    };
}
