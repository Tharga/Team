using System.Globalization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// <see cref="ITeamMailStore"/> on MongoDB, paged by a cursor on received time so a large mailbox is never read whole.
/// </summary>
internal sealed class MongoTeamMailStore(IInboundMailCollection collection) : ITeamMailStore
{
    private static FilterDefinitionBuilder<InboundMailEntity> Filter => Builders<InboundMailEntity>.Filter;

    public async Task<bool> AddAsync(InboundMailItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            await collection.AddAsync(ToEntity(item));
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<InboundMailItem> GetAsync(string teamKey, string mailId, CancellationToken cancellationToken = default) =>
        ToItem(await collection.GetOneAsync(OfTeam(teamKey, mailId)));

    public Task<InboundMailPage> ListAsync(string teamKey, InboundMailFilter filter, string cursor, int pageSize, CancellationToken cancellationToken = default) =>
        PageAsync(Filter.And(Filter.Eq(x => x.TeamKey, teamKey), Handled(filter)), cursor, pageSize);

    public async Task<int> CountUnhandledAsync(string teamKey, CancellationToken cancellationToken = default) =>
        (int)await collection.CountAsync(Filter.And(Filter.Eq(x => x.TeamKey, teamKey), Handled(InboundMailFilter.Unhandled)));

    public async Task<InboundMailItem> SetOutcomeAsync(string teamKey, string mailId, string outcome, MailThreadOwner owner, string handledBy, DateTime handledAt, CancellationToken cancellationToken = default)
    {
        var update = outcome == null
            ? Builders<InboundMailEntity>.Update.Unset(x => x.Outcome).Unset(x => x.HandledAt).Unset(x => x.HandledBy)
            : Builders<InboundMailEntity>.Update.Set(x => x.Outcome, outcome).Set(x => x.HandledAt, handledAt).Set(x => x.HandledBy, handledBy);

        update = owner == null
            ? update.Unset(x => x.OwnerType).Unset(x => x.OwnerId)
            : update.Set(x => x.OwnerType, owner.Type).Set(x => x.OwnerId, owner.Id);

        var result = await collection.UpdateOneAsync(OfTeam(teamKey, mailId), update);
        return result?.Before == null ? null : await GetAsync(teamKey, mailId, cancellationToken);
    }

    public async Task<InboundMailItem> GetUnroutedAsync(string mailId, CancellationToken cancellationToken = default) =>
        ToItem(await collection.GetOneAsync(Unrouted(mailId)));

    public Task<InboundMailPage> ListUnroutedAsync(string cursor, int pageSize, CancellationToken cancellationToken = default) =>
        PageAsync(Filter.Exists(x => x.TeamKey, false), cursor, pageSize);

    public async Task<int> CountUnroutedAsync(CancellationToken cancellationToken = default) =>
        (int)await collection.CountAsync(Filter.Exists(x => x.TeamKey, false));

    public async Task<InboundMailItem> RouteAsync(string mailId, string teamKey, CancellationToken cancellationToken = default)
    {
        var update = Builders<InboundMailEntity>.Update
            .Set(x => x.TeamKey, teamKey)
            .Set(x => x.Reason, MailRouteReason.Rerouted)
            .Unset(x => x.KeyRetiresAt);

        var result = await collection.UpdateOneAsync(Unrouted(mailId), update);
        return result?.Before == null ? null : await GetAsync(teamKey, mailId, cancellationToken);
    }

    public Task<string[]> DeleteUnroutedReceivedBeforeAsync(DateTime receivedBefore, CancellationToken cancellationToken = default) =>
        DeleteAsync(Filter.And(Filter.Exists(x => x.TeamKey, false), Filter.Lt(x => x.ReceivedAt, receivedBefore)));

    public Task<string[]> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default) =>
        DeleteAsync(Filter.Eq(x => x.TeamKey, teamKey));

    private async Task<string[]> DeleteAsync(FilterDefinition<InboundMailEntity> filter)
    {
        var deleted = new List<string>();
        await foreach (var entity in collection.GetAsync(filter))
        {
            await collection.DeleteOneAsync(entity.Id);
            deleted.Add(entity.MailId);
        }

        return [.. deleted];
    }

    private async Task<InboundMailPage> PageAsync(FilterDefinition<InboundMailEntity> filter, string cursor, int pageSize)
    {
        var size = Math.Clamp(pageSize, 1, 500);
        var options = new Options<InboundMailEntity>
        {
            Sort = Builders<InboundMailEntity>.Sort.Descending(x => x.ReceivedAt).Ascending(x => x.MailId),
            Limit = size + 1
        };

        var result = await collection.GetManyAsync(Filter.And(filter, After(cursor)), options);
        var page = result.Items.Take(size).ToArray();

        return new InboundMailPage
        {
            Items = [.. page.Select(ToItem)],
            NextCursor = result.Items.Count() > size ? CursorOf(page[^1]) : null
        };
    }

    internal static string CursorOf(InboundMailEntity entity) => $"{entity.ReceivedAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{entity.MailId}";

    internal static FilterDefinition<InboundMailEntity> After(string cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return Filter.Empty;

        var separator = cursor.IndexOf(':');
        if (separator < 1 || !long.TryParse(cursor[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) throw new ArgumentException($"'{cursor}' is not a cursor this mailbox issued.", nameof(cursor));

        var receivedAt = new DateTime(ticks, DateTimeKind.Utc);
        var mailId = cursor[(separator + 1)..];

        return Filter.Or(
            Filter.Lt(x => x.ReceivedAt, receivedAt),
            Filter.And(Filter.Eq(x => x.ReceivedAt, receivedAt), Filter.Gt(x => x.MailId, mailId)));
    }

    private static FilterDefinition<InboundMailEntity> Handled(InboundMailFilter filter) => filter switch
    {
        InboundMailFilter.Unhandled => Filter.Exists(x => x.Outcome, false),
        InboundMailFilter.Handled => Filter.Exists(x => x.Outcome, true),
        _ => Filter.Empty
    };

    private static FilterDefinition<InboundMailEntity> OfTeam(string teamKey, string mailId) =>
        Filter.And(Filter.Eq(x => x.TeamKey, teamKey), Filter.Eq(x => x.MailId, mailId));

    private static FilterDefinition<InboundMailEntity> Unrouted(string mailId) =>
        Filter.And(Filter.Exists(x => x.TeamKey, false), Filter.Eq(x => x.MailId, mailId));

    internal static InboundMailEntity ToEntity(InboundMailItem item) => new()
    {
        MailId = item.Id,
        TeamKey = item.TeamKey,
        ItemKey = item.ItemKey,
        Envelope = new MailEnvelopeEntity
        {
            MessageId = item.Envelope.MessageId,
            From = item.Envelope.From,
            FromName = item.Envelope.FromName,
            To = item.Envelope.To ?? [],
            Cc = item.Envelope.Cc ?? [],
            DeliveredTo = item.Envelope.DeliveredTo,
            Subject = item.Envelope.Subject,
            SentAt = item.Envelope.SentAt,
            InReplyTo = item.Envelope.InReplyTo,
            References = item.Envelope.References ?? [],
            HasAttachments = item.Envelope.HasAttachments,
            IsAutomated = item.Envelope.IsAutomated
        },
        Reason = item.Reason,
        ReceivedAt = item.ReceivedAt,
        KeyRetiresAt = item.KeyRetiresAt,
        OwnerType = item.Owner?.Type,
        OwnerId = item.Owner?.Id,
        Outcome = item.Outcome,
        HandledAt = item.HandledAt,
        HandledBy = item.HandledBy
    };

    internal static InboundMailItem ToItem(InboundMailEntity entity) => entity == null ? null : new()
    {
        Id = entity.MailId,
        TeamKey = entity.TeamKey,
        ItemKey = entity.ItemKey,
        Envelope = new MailEnvelope
        {
            MessageId = entity.Envelope.MessageId,
            From = entity.Envelope.From,
            FromName = entity.Envelope.FromName,
            To = entity.Envelope.To ?? [],
            Cc = entity.Envelope.Cc ?? [],
            DeliveredTo = entity.Envelope.DeliveredTo,
            Subject = entity.Envelope.Subject,
            SentAt = entity.Envelope.SentAt,
            InReplyTo = entity.Envelope.InReplyTo,
            References = entity.Envelope.References ?? [],
            HasAttachments = entity.Envelope.HasAttachments,
            IsAutomated = entity.Envelope.IsAutomated
        },
        Reason = entity.Reason,
        ReceivedAt = entity.ReceivedAt,
        KeyRetiresAt = entity.KeyRetiresAt,
        Owner = entity.OwnerType == null ? null : new MailThreadOwner { Type = entity.OwnerType, Id = entity.OwnerId },
        Outcome = entity.Outcome,
        HandledAt = entity.HandledAt,
        HandledBy = entity.HandledBy
    };
}
