using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// MongoDB adapter for <see cref="ISupportCaseStore"/>.
/// </summary>
/// <remarks>
/// <b>Every query leads with the team.</b> The port takes a team key on every method precisely so this
/// cannot be forgotten, and each filter here starts from it — a case id on its own is never a lookup key.
/// That is what makes reading another tenant's case hard to write rather than merely discouraged.
/// <para>
/// <b>The two composite operations are single document writes.</b> Creating a case with its first message is
/// one insert; closing a case sets the status and pushes the closure entry in one update. Neither can
/// half-apply, which is the atomicity the port's signatures promise.
/// </para>
/// </remarks>
internal sealed class MongoSupportCaseStore(ISupportCaseRepositoryCollection collection) : ISupportCaseStore
{
    public async Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(Case(teamKey, caseId));

        return entity == null ? null : ToCase(entity);
    }

    public Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        return PageAsync(Builders<SupportCaseEntity>.Filter.Eq(x => x.TeamKey, teamKey), cursor, pageSize);
    }

    public Task<SupportCasePage> GetCasesByAuthorAsync(string teamKey, string authorIdentity, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportCaseEntity>.Filter.And(
            Builders<SupportCaseEntity>.Filter.Eq(x => x.TeamKey, teamKey),
            Builders<SupportCaseEntity>.Filter.Eq(x => x.AuthorIdentity, authorIdentity));

        return PageAsync(filter, cursor, pageSize);
    }

    public async Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(Case(teamKey, caseId));
        if (entity == null) return new SupportMessagePage { Items = [] };

        var after = ParseSequence(cursor);

        var items = entity.Messages
            .Where(x => x.Sequence > after)
            .OrderBy(x => x.Sequence)
            .Take(pageSize)
            .Select(ToMessage)
            .ToArray();

        var last = items.Length == 0 ? 0 : items[items.Length - 1].Sequence;
        var more = items.Length == pageSize && entity.Messages.Any(x => x.Sequence > last);

        return new SupportMessagePage
        {
            Items = items,
            NextCursor = more ? last.ToString() : null
        };
    }

    public async Task AddCaseAsync(SupportCase supportCase, SupportMessage firstMessage, CancellationToken cancellationToken = default)
    {
        RequireWithinLength(firstMessage);

        await collection.AddAsync(new SupportCaseEntity
        {
            CaseId = supportCase.Id,
            TeamKey = supportCase.TeamKey,
            AuthorIdentity = supportCase.AuthorIdentity,
            AuthorName = supportCase.AuthorName,
            Subject = supportCase.Subject,
            Status = SupportCaseStatus.Open,
            CreatedAt = supportCase.CreatedAt,
            Messages = [ToEntity(firstMessage)],

            // A case opens with its author's own message, so it is waiting on support from the moment it
            // exists.
            LastMessageFromAuthor = true,
            LastMessageAt = firstMessage.SentAt
        });
    }

    public async Task AppendMessageAsync(string teamKey, string caseId, SupportMessage message, CancellationToken cancellationToken = default)
    {
        RequireWithinLength(message);

        var entity = await RequireCaseAsync(teamKey, caseId);
        RequireRoom(entity);

        var update = Builders<SupportCaseEntity>.Update
            .Push(x => x.Messages, ToEntity(message with { Sequence = NextSequence(entity) }))
            .Set(x => x.LastMessageFromAuthor, message.AuthorIdentity == entity.AuthorIdentity)
            .Set(x => x.LastMessageAt, message.SentAt);

        await UpdateAsync(teamKey, caseId, update);
    }

    public async Task CloseCaseAsync(string teamKey, string caseId, DateTime closedAt, string closedBy, SupportMessage closureMessage, CancellationToken cancellationToken = default)
    {
        var entity = await RequireCaseAsync(teamKey, caseId);
        RequireRoom(entity);

        var update = Builders<SupportCaseEntity>.Update
            .Set(x => x.Status, SupportCaseStatus.Closed)
            .Set(x => x.ClosedAt, closedAt)
            .Set(x => x.ClosedBy, closedBy)
            .Set(x => x.LastMessageFromAuthor, false)
            .Set(x => x.LastMessageAt, closureMessage.SentAt)
            .Push(x => x.Messages, ToEntity(closureMessage with { Sequence = NextSequence(entity) }));

        await UpdateAsync(teamKey, caseId, update);
    }

    public async Task ReopenCaseAsync(string teamKey, string caseId, SupportMessage reopenMessage, CancellationToken cancellationToken = default)
    {
        var entity = await RequireCaseAsync(teamKey, caseId);
        RequireRoom(entity);

        var update = Builders<SupportCaseEntity>.Update
            .Set(x => x.Status, SupportCaseStatus.Open)
            .Set(x => x.ClosedAt, null)
            .Set(x => x.ClosedBy, null)

            // The reopen entry is the toolkit's, not the customer's, so the case is not waiting on support
            // until somebody actually writes to it.
            .Set(x => x.LastMessageFromAuthor, false)
            .Set(x => x.LastMessageAt, reopenMessage.SentAt)
            .Push(x => x.Messages, ToEntity(reopenMessage with { Sequence = NextSequence(entity) }));

        await UpdateAsync(teamKey, caseId, update);
    }

    /// <remarks>
    /// Narrowed by indexed fields, then finished on the transcript that came with each document. The final
    /// check cannot be a filter — it is "what kind of entry is the last element of an array" — but it costs
    /// nothing here, because the array is already in hand.
    /// </remarks>
    public async Task<SupportCase[]> GetCasesForInactivityCloseAsync(DateTime lastActivityBefore, int limit, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportCaseEntity>.Filter.And(
            Builders<SupportCaseEntity>.Filter.Eq(x => x.Status, SupportCaseStatus.Open),

            // Support answered, or the toolkit wrote something. The transcript tail below tells the two apart.
            Builders<SupportCaseEntity>.Filter.Eq(x => x.LastMessageFromAuthor, false),
            Builders<SupportCaseEntity>.Filter.Lt(x => x.LastMessageAt, lastActivityBefore));

        var due = new List<SupportCase>();

        await foreach (var entity in collection.GetAsync(filter).WithCancellation(cancellationToken))
        {
            if (!SupportWroteLast(entity)) continue;

            due.Add(ToCase(entity));

            if (due.Count >= limit) break;
        }

        return [.. due];
    }

    /// <summary>
    /// Whether a person other than the author wrote the newest entry — as opposed to the toolkit itself.
    /// </summary>
    internal static bool SupportWroteLast(SupportCaseEntity entity)
    {
        var last = entity.Messages.Length == 0
            ? null
            : entity.Messages.OrderBy(x => x.Sequence).Last();

        return last is { Kind: SupportMessageKind.User } && last.AuthorIdentity != entity.AuthorIdentity;
    }

    public async Task<bool> TryCloseForInactivityAsync(string teamKey, string caseId, DateTime closedAt, SupportMessage closureMessage, CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(Case(teamKey, caseId));

        if (entity == null || entity.Status != SupportCaseStatus.Open) return false;

        // Conditional on the status this read observed. Two sweeps racing means the second matches nothing
        // and reports that it closed nothing, rather than appending a second closure entry.
        var filter = Builders<SupportCaseEntity>.Filter.And(
            Case(teamKey, caseId),
            Builders<SupportCaseEntity>.Filter.Eq(x => x.Status, SupportCaseStatus.Open));

        var update = Builders<SupportCaseEntity>.Update
            .Set(x => x.Status, SupportCaseStatus.Closed)
            .Set(x => x.ClosedAt, closedAt)
            .Set(x => x.ClosedBy, SupportCaseActors.AutoClose)
            .Set(x => x.LastMessageFromAuthor, false)
            .Set(x => x.LastMessageAt, closureMessage.SentAt)
            .Push(x => x.Messages, ToEntity(closureMessage with { Sequence = NextSequence(entity) }));

        var result = await collection.UpdateOneAsync(filter, update);

        // The driver reports the document as it was, not a count. No before-image means the filter matched
        // nothing — which here means another sweep closed it first.
        return result?.Before != null;
    }

    public async Task<SupportCase> GetCaseByBindingAsync(SupportChannelType channelType, string externalId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportCaseEntity>.Filter.ElemMatch(
            x => x.Bindings,
            Builders<SupportChannelBindingEntity>.Filter.And(
                Builders<SupportChannelBindingEntity>.Filter.Eq(x => x.ChannelType, channelType),
                Builders<SupportChannelBindingEntity>.Filter.Eq(x => x.ExternalId, externalId)));

        var entity = await collection.GetOneAsync(filter);

        return entity == null ? null : ToCase(entity);
    }

    public async Task<SupportCase> GetCaseByIdAsync(string caseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId)) return null;

        var entity = await collection.GetOneAsync(Builders<SupportCaseEntity>.Filter.Eq(x => x.CaseId, caseId));

        return entity == null ? null : ToCase(entity);
    }

    public async Task MarkReadAsync(string teamKey, string caseId, string identity, int sequence, CancellationToken cancellationToken = default)
    {
        var entity = await RequireCaseAsync(teamKey, caseId);

        var existing = entity.Reads ?? [];
        var current = Array.Find(existing, x => x.Identity == identity);

        // Never move backwards. Two tabs open on the same case would otherwise let the one showing an older
        // page reset the marker and light the indicator again.
        if (current != null && current.LastReadSequence >= sequence) return;

        var read = new SupportCaseReadEntity
        {
            Identity = identity,
            LastReadSequence = sequence,
            ReadAt = DateTime.UtcNow
        };

        // Replaced in place rather than appended, so opening a case repeatedly leaves one entry.
        var reads = existing.Where(x => x.Identity != identity).Append(read).ToArray();

        await UpdateAsync(teamKey, caseId, Builders<SupportCaseEntity>.Update.Set(x => x.Reads, reads));
    }

    /// <remarks>
    /// Counted over this person's own cases rather than as a single server-side filter, and the difference is
    /// deliberate. "Has entries beyond my last-read marker" is a comparison between two fields of the same
    /// document, which a plain filter cannot express. The set being scanned is one person's own cases, which
    /// is small and bounded - unlike the awaiting-support count, which is why that one is denormalized
    /// instead.
    /// </remarks>
    public async Task<int> GetUnreadCountAsync(string teamKey, string identity, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportCaseEntity>.Filter.And(
            Builders<SupportCaseEntity>.Filter.Eq(x => x.TeamKey, teamKey),
            Builders<SupportCaseEntity>.Filter.Eq(x => x.AuthorIdentity, identity));

        var unread = 0;
        await foreach (var entity in collection.GetAsync(filter).WithCancellation(cancellationToken))
        {
            var read = Array.Find(entity.Reads ?? [], x => x.Identity == identity);

            if ((read?.LastReadSequence ?? 0) < entity.Messages.Length) unread++;
        }

        return unread;
    }

    public async Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportCaseEntity>.Filter.And(
            Builders<SupportCaseEntity>.Filter.Eq(x => x.TeamKey, teamKey),
            Builders<SupportCaseEntity>.Filter.Eq(x => x.Status, SupportCaseStatus.Open),
            Builders<SupportCaseEntity>.Filter.Eq(x => x.LastMessageFromAuthor, true));

        return (int)await collection.CountAsync(filter);
    }

    public async Task AddBindingAsync(string teamKey, string caseId, SupportChannelBinding binding, CancellationToken cancellationToken = default)
    {
        await RequireCaseAsync(teamKey, caseId);

        var update = Builders<SupportCaseEntity>.Update
            .Push(x => x.Bindings, new SupportChannelBindingEntity
            {
                ChannelType = binding.ChannelType,
                ExternalId = binding.ExternalId,
                Address = binding.Address
            });

        await UpdateAsync(teamKey, caseId, update);
    }

    /// <remarks>
    /// The transcript is an embedded array, so the entry is rewritten in place by index rather than by a
    /// positional operator - the sequence is its position, and rewriting the whole array would race with a
    /// reply arriving at the same moment.
    /// </remarks>
    public async Task SetMessageDeliveryAsync(string teamKey, string caseId, int sequence, SupportMessageDelivery delivery, CancellationToken cancellationToken = default)
    {
        var entity = await RequireCaseAsync(teamKey, caseId);

        var index = Array.FindIndex(entity.Messages, x => x.Sequence == sequence);
        if (index < 0) return;

        var update = Builders<SupportCaseEntity>.Update
            .Set($"Messages.{index}.Delivery", delivery.ToString());

        await UpdateAsync(teamKey, caseId, update);
    }

    public async Task<int> DeleteCasesForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportCaseEntity>.Filter.Eq(x => x.TeamKey, teamKey);

        var removed = 0;
        await foreach (var entity in collection.GetAsync(filter).WithCancellation(cancellationToken))
        {
            await collection.DeleteOneAsync(Case(entity.TeamKey, entity.CaseId));
            removed++;
        }

        return removed;
    }

    private async Task<SupportCasePage> PageAsync(FilterDefinition<SupportCaseEntity> filter, string cursor, int pageSize)
    {
        var all = new List<SupportCaseEntity>();
        await foreach (var entity in collection.GetAsync(filter)) all.Add(entity);

        var ordered = all.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.CaseId).ToList();

        var start = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            var index = ordered.FindIndex(x => x.CaseId == cursor);
            start = index < 0 ? ordered.Count : index + 1;
        }

        var items = ordered.Skip(start).Take(pageSize).Select(ToCase).ToArray();
        var more = start + items.Length < ordered.Count;

        return new SupportCasePage
        {
            Items = items,
            NextCursor = more && items.Length > 0 ? items[items.Length - 1].Id : null
        };
    }

    private async Task<SupportCaseEntity> RequireCaseAsync(string teamKey, string caseId)
    {
        var entity = await collection.GetOneAsync(Case(teamKey, caseId));

        if (entity == null)
            throw new InvalidOperationException($"Support case {caseId} was not found in team {teamKey}.");

        return entity;
    }

    private Task UpdateAsync(string teamKey, string caseId, UpdateDefinition<SupportCaseEntity> update)
    {
        return collection.UpdateOneAsync(Case(teamKey, caseId), update);
    }

    private static FilterDefinition<SupportCaseEntity> Case(string teamKey, string caseId) =>
        Builders<SupportCaseEntity>.Filter.And(
            Builders<SupportCaseEntity>.Filter.Eq(x => x.TeamKey, teamKey),
            Builders<SupportCaseEntity>.Filter.Eq(x => x.CaseId, caseId));

    private static int NextSequence(SupportCaseEntity entity) =>
        entity.Messages.Length == 0 ? 1 : entity.Messages.Max(x => x.Sequence) + 1;

    private static void RequireRoom(SupportCaseEntity entity)
    {
        if (entity.Messages.Length >= SupportCaseLimits.MaxMessagesPerCase)
            throw new InvalidOperationException(
                $"Support case {entity.CaseId} already holds {entity.Messages.Length} messages, which is the " +
                $"limit of {SupportCaseLimits.MaxMessagesPerCase}. Raise a new case rather than extending this one.");
    }

    private static void RequireWithinLength(SupportMessage message)
    {
        if (message.Body != null && message.Body.Length > SupportCaseLimits.MaxMessageLength)
            throw new InvalidOperationException(
                $"A support message is {message.Body.Length} characters, which exceeds the limit of " +
                $"{SupportCaseLimits.MaxMessageLength}. Attach or link long content instead of pasting it.");
    }

    private static int ParseSequence(string cursor) =>
        int.TryParse(cursor, out var value) ? value : 0;

    private static SupportCase ToCase(SupportCaseEntity entity) => new()
    {
        Id = entity.CaseId,
        TeamKey = entity.TeamKey,
        AuthorIdentity = entity.AuthorIdentity,
        AuthorName = entity.AuthorName,
        Subject = entity.Subject,
        Status = entity.Status,
        CreatedAt = entity.CreatedAt,
        ClosedAt = entity.ClosedAt,
        ClosedBy = entity.ClosedBy,
        MessageCount = entity.Messages.Length,
        Bindings = entity.Bindings == null
            ? []
            : [.. entity.Bindings.Select(x => new SupportChannelBinding { ChannelType = x.ChannelType, ExternalId = x.ExternalId, Address = x.Address })]
    };

    private static SupportMessage ToMessage(SupportMessageEntity entity) => new()
    {
        Sequence = entity.Sequence,
        Kind = entity.Kind,
        AuthorIdentity = entity.AuthorIdentity,
        AuthorName = entity.AuthorName,
        Body = entity.Body,
        SentAt = entity.SentAt,
        Delivery = entity.Delivery,
        Source = entity.Source
    };

    private static SupportMessageEntity ToEntity(SupportMessage message) => new()
    {
        Sequence = message.Sequence,
        Kind = message.Kind,
        AuthorIdentity = message.AuthorIdentity,
        AuthorName = message.AuthorName,
        Body = message.Body,
        SentAt = message.SentAt,
        Delivery = message.Delivery,
        Source = message.Source
    };
}
