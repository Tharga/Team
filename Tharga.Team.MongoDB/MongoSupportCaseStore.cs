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
            Messages = [ToEntity(firstMessage)]
        });
    }

    public async Task AppendMessageAsync(string teamKey, string caseId, SupportMessage message, CancellationToken cancellationToken = default)
    {
        RequireWithinLength(message);

        var entity = await RequireCaseAsync(teamKey, caseId);
        RequireRoom(entity);

        var update = Builders<SupportCaseEntity>.Update
            .Push(x => x.Messages, ToEntity(message with { Sequence = NextSequence(entity) }));

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
            .Push(x => x.Messages, ToEntity(closureMessage with { Sequence = NextSequence(entity) }));

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
            : [.. entity.Bindings.Select(x => new SupportChannelBinding { ChannelType = x.ChannelType, ExternalId = x.ExternalId })]
    };

    private static SupportMessage ToMessage(SupportMessageEntity entity) => new()
    {
        Sequence = entity.Sequence,
        Kind = entity.Kind,
        AuthorIdentity = entity.AuthorIdentity,
        AuthorName = entity.AuthorName,
        Body = entity.Body,
        SentAt = entity.SentAt
    };

    private static SupportMessageEntity ToEntity(SupportMessage message) => new()
    {
        Sequence = message.Sequence,
        Kind = message.Kind,
        AuthorIdentity = message.AuthorIdentity,
        AuthorName = message.AuthorName,
        Body = message.Body,
        SentAt = message.SentAt
    };
}
