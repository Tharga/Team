using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserNoticeOutbox"/>.</summary>
/// <remarks>
/// <b>Every conditional write is one find-and-modify on the server</b>, passing <see cref="EMode.FirstOrDefault"/>, so
/// "due and nobody holds it" and "this claimant still holds it" are decided atomically.
/// <para>
/// <b>A stream's due time only moves earlier while notices are added</b> (<c>$min</c>), so a burst keeps the time its
/// first notice set. Each addition also raises the stream's version, and a release sets the next due time only if the
/// version is the one it counted the waiting notices under, so a notice added in between is never left without one.
/// </para>
/// </remarks>
internal sealed class MongoUserNoticeOutbox(IUserNoticeCollection notices, IUserNoticeItemCollection items, IUserNoticeStreamCollection streams) : IUserNoticeOutbox
{
    /// <summary>The due time of a stream with nothing waiting.</summary>
    internal static readonly DateTime Idle = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    private const int WriteAttempts = 5;

    private static readonly FilterDefinitionBuilder<UserNoticeEntity> NoticeFilter = Builders<UserNoticeEntity>.Filter;
    private static readonly FilterDefinitionBuilder<UserNoticeItemEntity> ItemFilter = Builders<UserNoticeItemEntity>.Filter;
    private static readonly FilterDefinitionBuilder<UserNoticeStreamEntity> StreamFilter = Builders<UserNoticeStreamEntity>.Filter;

    public async Task<string> AddAsync(UserNoticeOccurrence occurrence, DateTime raisedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        var entity = ToEntity(occurrence, raisedAt);
        await notices.AddAsync(entity);

        return entity.Id.ToString();
    }

    public async Task<UserNoticeRecord> ClaimNextAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimant);

        var until = now + lease;
        var update = Builders<UserNoticeEntity>.Update.Set(x => x.ClaimedBy, claimant).Set(x => x.ClaimedUntil, until).Inc(x => x.Attempts, 1);

        var before = (await notices.UpdateOneAsync(Claimable(now), update, new OneOption<UserNoticeEntity>
        {
            Mode = EMode.FirstOrDefault,
            Sort = Builders<UserNoticeEntity>.Sort.Ascending(x => x.NextAttemptAt)
        }))?.Before;

        return before == null ? null : ToRecord(before with { ClaimedBy = claimant, ClaimedUntil = until, Attempts = before.Attempts + 1 });
    }

    public Task<bool> MarkDistributedAsync(string id, string claimant, int recipients, DateTime at, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, Builders<UserNoticeEntity>.Update.Set(x => x.Status, UserNoticeStatus.Distributed).Set(x => x.RecipientCount, recipients).Set(x => x.CompletedAt, at));

    public Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, Builders<UserNoticeEntity>.Update.Set(x => x.Status, UserNoticeStatus.Failed).Set(x => x.Reason, reason).Set(x => x.CompletedAt, at));

    public Task<bool> PostponeAsync(string id, string claimant, string reason, DateTime nextAttemptAt, CancellationToken cancellationToken = default)
        => CompleteAsync(id, claimant, Builders<UserNoticeEntity>.Update.Set(x => x.NextAttemptAt, nextAttemptAt).Set(x => x.Reason, reason));

    public async Task<UserNoticeRecord> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return null;

        var entity = await notices.GetOneAsync(x => x.Id == objectId, cancellationToken: cancellationToken);

        return entity == null ? null : ToRecord(entity);
    }

    /// <remarks>The notice first, then its stream: a stream made due with nothing in it only costs an empty pass, while a notice in a stream that is never due would wait for ever.</remarks>
    public async Task<bool> AddItemAsync(UserNoticeItem item, DateTime dueBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        try
        {
            await items.AddAsync(ToItemEntity(item));
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await items.GetOneAsync(OfOccurrence(item.OccurrenceKey, item.UserKey), cancellationToken: cancellationToken);
            if (existing?.Status == UserNoticeItemStatus.Waiting) await MakeDueAsync(existing.UserKey, existing.Stream, dueBy);

            return false;
        }

        await MakeDueAsync(item.UserKey, item.Stream, dueBy);
        return true;
    }

    public async Task<UserNoticeStreamClaim> ClaimDueStreamAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimant);

        var update = Builders<UserNoticeStreamEntity>.Update.Set(x => x.ClaimedBy, claimant).Set(x => x.ClaimedUntil, now + lease);

        var before = (await streams.UpdateOneAsync(Due(now), update, new OneOption<UserNoticeStreamEntity>
        {
            Mode = EMode.FirstOrDefault,
            Sort = Builders<UserNoticeStreamEntity>.Sort.Ascending(x => x.DueAt)
        }))?.Before;

        return before == null ? null : new UserNoticeStreamClaim
        {
            UserKey = before.UserKey,
            Stream = before.Stream,
            MailKey = before.MailKey,
            MailItemIds = before.MailItemIds ?? []
        };
    }

    public async Task<UserNoticeItem[]> GetWaitingAsync(string userKey, string stream, int max, CancellationToken cancellationToken = default)
    {
        var found = await items.GetManyAsync(Waiting(userKey, stream), new Options<UserNoticeItemEntity>
        {
            Sort = Builders<UserNoticeItemEntity>.Sort.Ascending(x => x.RaisedAt),
            Limit = Math.Max(1, max)
        }, cancellationToken);

        return [.. (found?.Items ?? []).Select(ToItem)];
    }

    public async Task<UserNoticeItem[]> GetItemsAsync(string[] ids, CancellationToken cancellationToken = default)
    {
        var objectIds = ObjectIds(ids);
        if (objectIds.Length == 0) return [];

        var found = await items.GetManyAsync(ItemFilter.In(x => x.Id, objectIds), cancellationToken: cancellationToken);

        return [.. (found?.Items ?? []).Select(ToItem)];
    }

    public async Task<bool> BeginMailAsync(string userKey, string stream, string claimant, string mailKey, string[] itemIds, CancellationToken cancellationToken = default)
    {
        var update = Builders<UserNoticeStreamEntity>.Update.Set(x => x.MailKey, mailKey).Set(x => x.MailItemIds, itemIds ?? []);

        return (await streams.UpdateOneAsync(HeldStream(userKey, stream, claimant), update, OneOption<UserNoticeStreamEntity>.FirstOrDefault))?.Before != null;
    }

    public async Task CompleteItemsAsync(string[] itemIds, UserNoticeItemStatus status, string mailId, string reason, DateTime at, CancellationToken cancellationToken = default)
    {
        var objectIds = ObjectIds(itemIds);
        if (objectIds.Length == 0) return;

        var update = Builders<UserNoticeItemEntity>.Update.Set(x => x.Status, status).Set(x => x.CompletedAt, at);
        update = mailId == null ? update : update.Set(x => x.MailId, mailId);
        update = reason == null ? update : update.Set(x => x.Reason, reason);

        await items.UpdateManyAsync(ItemFilter.In(x => x.Id, objectIds) & ItemFilter.Eq(x => x.Status, UserNoticeItemStatus.Waiting), update);
    }

    public async Task<bool> ReleaseStreamAsync(string userKey, string stream, string claimant, DateTime nextDueWhenWaiting, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < WriteAttempts; attempt++)
        {
            var current = await streams.GetOneAsync(HeldStream(userKey, stream, claimant), OneOption<UserNoticeStreamEntity>.FirstOrDefault, cancellationToken);
            if (current == null) return false;

            var waiting = await items.CountAsync(Waiting(userKey, stream), cancellationToken) > 0;
            var released = await streams.UpdateOneAsync(HeldStream(userKey, stream, claimant) & StreamFilter.Eq(x => x.Version, current.Version),
                Release(waiting ? nextDueWhenWaiting : Idle), OneOption<UserNoticeStreamEntity>.FirstOrDefault);

            if (released?.Before != null) return true;
        }

        return (await streams.UpdateOneAsync(HeldStream(userKey, stream, claimant), Release(nextDueWhenWaiting), OneOption<UserNoticeStreamEntity>.FirstOrDefault))?.Before != null;
    }

    /// <summary>Moves the stream's due time to <paramref name="dueBy"/> if that is earlier, raising its version, and creates the stream when it has none.</summary>
    private async Task MakeDueAsync(string userKey, string stream, DateTime dueBy)
    {
        for (var attempt = 0; attempt < WriteAttempts; attempt++)
        {
            if ((await streams.UpdateOneAsync(OfStream(userKey, stream), MakeDue(dueBy), OneOption<UserNoticeStreamEntity>.FirstOrDefault))?.Before != null) return;

            if (await streams.TryAddAsync(new UserNoticeStreamEntity { Id = ObjectId.GenerateNewId(), UserKey = userKey, Stream = stream, DueAt = dueBy, Version = 1 })) return;
        }

        throw new InvalidOperationException($"The notice stream '{stream}' of user '{userKey}' could be neither updated nor created.");
    }

    /// <summary>Waiting to be distributed, due, and either never claimed or held by a lease that has run out.</summary>
    internal static FilterDefinition<UserNoticeEntity> Claimable(DateTime now)
        => NoticeFilter.Eq(x => x.Status, UserNoticeStatus.Pending)
           & NoticeFilter.Lte(x => x.NextAttemptAt, now)
           & (NoticeFilter.Eq(x => x.ClaimedUntil, null) | NoticeFilter.Lte(x => x.ClaimedUntil, now));

    /// <summary>This occurrence, still waiting and still held by this claimant.</summary>
    internal static FilterDefinition<UserNoticeEntity> HeldBy(ObjectId id, string claimant)
        => NoticeFilter.Eq(x => x.Id, id) & NoticeFilter.Eq(x => x.Status, UserNoticeStatus.Pending) & NoticeFilter.Eq(x => x.ClaimedBy, claimant);

    /// <summary>A stream that is due and nobody holds, or whose holder's lease has run out.</summary>
    internal static FilterDefinition<UserNoticeStreamEntity> Due(DateTime now)
        => StreamFilter.Lte(x => x.DueAt, now) & (StreamFilter.Eq(x => x.ClaimedUntil, null) | StreamFilter.Lte(x => x.ClaimedUntil, now));

    /// <summary>This user's stream of this name.</summary>
    internal static FilterDefinition<UserNoticeStreamEntity> OfStream(string userKey, string stream)
        => StreamFilter.Eq(x => x.UserKey, userKey) & StreamFilter.Eq(x => x.Stream, stream);

    /// <summary>This user's stream, while this claimant holds it.</summary>
    internal static FilterDefinition<UserNoticeStreamEntity> HeldStream(string userKey, string stream, string claimant)
        => OfStream(userKey, stream) & StreamFilter.Eq(x => x.ClaimedBy, claimant);

    /// <summary>The due time moved earlier only, and the version raised.</summary>
    internal static UpdateDefinition<UserNoticeStreamEntity> MakeDue(DateTime dueBy)
        => Builders<UserNoticeStreamEntity>.Update.Min(x => x.DueAt, dueBy).Inc(x => x.Version, 1);

    /// <summary>The stream let go, due next at <paramref name="dueAt"/>, with no mail under way.</summary>
    internal static UpdateDefinition<UserNoticeStreamEntity> Release(DateTime dueAt)
        => Builders<UserNoticeStreamEntity>.Update
            .Set(x => x.DueAt, dueAt)
            .Unset(x => x.ClaimedBy)
            .Unset(x => x.ClaimedUntil)
            .Unset(x => x.MailKey)
            .Unset(x => x.MailItemIds);

    /// <summary>The notices waiting in this user's stream.</summary>
    internal static FilterDefinition<UserNoticeItemEntity> Waiting(string userKey, string stream)
        => ItemFilter.Eq(x => x.UserKey, userKey) & ItemFilter.Eq(x => x.Stream, stream) & ItemFilter.Eq(x => x.Status, UserNoticeItemStatus.Waiting);

    /// <summary>This user's notice of this occurrence.</summary>
    internal static FilterDefinition<UserNoticeItemEntity> OfOccurrence(string occurrenceKey, string userKey)
        => ItemFilter.Eq(x => x.OccurrenceKey, occurrenceKey) & ItemFilter.Eq(x => x.UserKey, userKey);

    private async Task<bool> CompleteAsync(string id, string claimant, UpdateDefinition<UserNoticeEntity> update)
    {
        if (!ObjectId.TryParse(id, out var objectId)) return false;

        var released = update.Unset(x => x.ClaimedBy).Unset(x => x.ClaimedUntil);

        return (await notices.UpdateOneAsync(HeldBy(objectId, claimant), released, OneOption<UserNoticeEntity>.FirstOrDefault))?.Before != null;
    }

    private static ObjectId[] ObjectIds(IEnumerable<string> ids)
        => [.. (ids ?? []).Select(x => ObjectId.TryParse(x, out var id) ? id : ObjectId.Empty).Where(x => x != ObjectId.Empty).Distinct()];

    internal static UserNoticeEntity ToEntity(UserNoticeOccurrence occurrence, DateTime raisedAt) => new()
    {
        Id = ObjectId.GenerateNewId(),
        NoticeKey = occurrence.NoticeKey,
        TeamKey = occurrence.TeamKey,
        SubjectId = occurrence.SubjectId,
        SubjectText = occurrence.SubjectText,
        Discriminator = occurrence.Discriminator,
        Values = new Dictionary<string, string>(occurrence.Values ?? new Dictionary<string, string>(), StringComparer.Ordinal),
        LinkPath = occurrence.LinkPath,
        Recipients = occurrence.Recipients ?? [],
        RaisedAt = raisedAt,
        Status = UserNoticeStatus.Pending,
        NextAttemptAt = raisedAt
    };

    internal static UserNoticeRecord ToRecord(UserNoticeEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        Occurrence = new UserNoticeOccurrence
        {
            NoticeKey = entity.NoticeKey,
            TeamKey = entity.TeamKey,
            SubjectId = entity.SubjectId,
            SubjectText = entity.SubjectText,
            Discriminator = entity.Discriminator,
            Values = entity.Values ?? [],
            LinkPath = entity.LinkPath,
            Recipients = entity.Recipients ?? []
        },
        Status = entity.Status,
        Attempts = entity.Attempts,
        RaisedAt = entity.RaisedAt,
        NextAttemptAt = entity.NextAttemptAt,
        CompletedAt = entity.CompletedAt,
        Recipients = entity.RecipientCount,
        Reason = entity.Reason
    };

    internal static UserNoticeItemEntity ToItemEntity(UserNoticeItem item) => new()
    {
        Id = ObjectId.GenerateNewId(),
        NoticeId = item.NoticeId,
        OccurrenceKey = item.OccurrenceKey,
        UserKey = item.UserKey,
        TeamKey = item.TeamKey,
        NoticeKey = item.NoticeKey,
        Category = item.Category,
        Stream = item.Stream,
        Delivery = item.Delivery,
        SubjectId = item.SubjectId,
        SubjectText = item.SubjectText,
        Values = new Dictionary<string, string>(item.Values ?? new Dictionary<string, string>(), StringComparer.Ordinal),
        LinkPath = item.LinkPath,
        RaisedAt = item.RaisedAt,
        Status = UserNoticeItemStatus.Waiting
    };

    internal static UserNoticeItem ToItem(UserNoticeItemEntity entity) => new()
    {
        Id = entity.Id.ToString(),
        NoticeId = entity.NoticeId,
        OccurrenceKey = entity.OccurrenceKey,
        UserKey = entity.UserKey,
        TeamKey = entity.TeamKey,
        NoticeKey = entity.NoticeKey,
        Category = entity.Category,
        Stream = entity.Stream,
        Delivery = entity.Delivery,
        SubjectId = entity.SubjectId,
        SubjectText = entity.SubjectText,
        Values = entity.Values ?? [],
        LinkPath = entity.LinkPath,
        RaisedAt = entity.RaisedAt,
        Status = entity.Status,
        MailId = entity.MailId,
        CompletedAt = entity.CompletedAt,
        Reason = entity.Reason
    };
}
