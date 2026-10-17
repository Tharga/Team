using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserMailOutbox"/>.</summary>
/// <remarks>
/// <b>Every conditional write is one find-and-modify on the server</b>, which is why each passes
/// <see cref="EMode.FirstOrDefault"/>: it is the only mode that sends the filter as the update's own, so "due and
/// nobody holds it" and "this claimant still holds it" are decided atomically rather than read and then written.
/// </remarks>
internal sealed class MongoUserMailOutbox(IUserMailCollection collection) : IUserMailOutbox
{
    private const string AttachmentContentPath = "Attachments.$[].Content";

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

    public Task<bool> MarkSentAsync(string id, string claimant, string messageId, DateTime at, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, UserMailStatus.Sent, null, messageId, at);

    public Task<bool> MarkSkippedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, UserMailStatus.Skipped, reason, null, at);

    public Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, UserMailStatus.Failed, reason, null, at);

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

    /// <summary>Queued, due, and either never claimed or held by a lease that has run out.</summary>
    internal static FilterDefinition<UserMailEntity> Claimable(DateTime now)
    {
        var filter = Builders<UserMailEntity>.Filter;

        return filter.Eq(x => x.Status, UserMailStatus.Queued)
               & filter.Lte(x => x.NextAttemptAt, now)
               & (filter.Eq(x => x.ClaimedUntil, null) | filter.Lte(x => x.ClaimedUntil, now));
    }

    /// <summary>This mail, still queued and still held by this claimant.</summary>
    internal static FilterDefinition<UserMailEntity> HeldBy(ObjectId id, string claimant)
    {
        var filter = Builders<UserMailEntity>.Filter;

        return filter.Eq(x => x.Id, id) & filter.Eq(x => x.Status, UserMailStatus.Queued) & filter.Eq(x => x.ClaimedBy, claimant);
    }

    /// <remarks>The files' content goes with the outcome: the record of what was sent is kept, the bytes are not.</remarks>
    private async Task<bool> CompleteAsync(string id, string claimant, UserMailStatus status, string reason, string messageId, DateTime at)
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

        return (await collection.UpdateOneAsync(HeldBy(objectId, claimant), update, OneOption<UserMailEntity>.FirstOrDefault))?.Before != null;
    }

    private Task<UserMailEntity> FindByDeduplicationKeyAsync(string key, CancellationToken cancellationToken)
        => collection.GetOneAsync(x => x.DeduplicationKey == key, cancellationToken: cancellationToken);

    internal static UserMailEntity ToEntity(UserMail mail, DateTime queuedAt) => new()
    {
        Id = ObjectId.GenerateNewId(),
        RecipientUserKey = mail.RecipientUserKey,
        Category = mail.Category,
        Subject = mail.Subject,
        BodyHtml = mail.BodyHtml,
        Body = mail.Body,
        Attachments = [.. (mail.Attachments ?? []).Select(x => new UserMailAttachmentEntity
        {
            FileName = x.FileName,
            ContentType = x.ContentType,
            Content = x.Content,
            Size = x.Content?.LongLength ?? x.Size,
            ContentId = x.ContentId
        })],
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
            Category = entity.Category,
            Subject = entity.Subject,
            BodyHtml = entity.BodyHtml,
            Body = entity.Body,
            Attachments = [.. (entity.Attachments ?? []).Select(x => new UserMailAttachment
            {
                FileName = x.FileName,
                ContentType = x.ContentType,
                Content = x.Content,
                Size = x.Size,
                ContentId = x.ContentId
            })],
            ReplyTo = entity.ReplyTo,
            DeduplicationKey = entity.DeduplicationKey
        },
        Status = entity.Status,
        Attempts = entity.Attempts,
        QueuedAt = entity.QueuedAt,
        NextAttemptAt = entity.NextAttemptAt,
        ClaimedBy = entity.ClaimedBy,
        ClaimedUntil = entity.ClaimedUntil,
        CompletedAt = entity.CompletedAt,
        Reason = entity.Reason,
        MessageId = entity.MessageId
    };
}
