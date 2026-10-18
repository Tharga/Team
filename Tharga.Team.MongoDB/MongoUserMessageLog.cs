using System.Globalization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserMessageLog"/>, paged newest first by a cursor of send time and id.</summary>
internal sealed class MongoUserMessageLog(IUserMessageCollection collection) : IUserMessageLog
{
    private static readonly FilterDefinitionBuilder<UserMessageEntity> Filter = Builders<UserMessageEntity>.Filter;

    public Task AddAsync(UserMessageLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return collection.AddAsync(ToEntity(entry));
    }

    public async Task<UserMessageLogEntry> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var entity = await collection.GetOneAsync(x => x.MessageId == id, cancellationToken: cancellationToken);

        return entity == null ? null : ToEntry(entity);
    }

    public async Task<UserMessageLogPage> ListAsync(string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(pageSize, 1, 500);
        var options = new Options<UserMessageEntity>
        {
            Sort = Builders<UserMessageEntity>.Sort.Descending(x => x.SentAt).Ascending(x => x.MessageId),
            Limit = size + 1
        };

        var result = await collection.GetManyAsync(After(cursor), options, cancellationToken);
        var page = (result?.Items ?? []).Take(size).ToArray();

        return new UserMessageLogPage
        {
            Items = [.. page.Select(ToEntry)],
            NextCursor = (result?.Items ?? []).Count() > size ? CursorOf(page[^1]) : null
        };
    }

    internal static string CursorOf(UserMessageEntity entity) => $"{entity.SentAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{entity.MessageId}";

    /// <exception cref="ArgumentException">The cursor was not issued by this log.</exception>
    internal static FilterDefinition<UserMessageEntity> After(string cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return Filter.Empty;

        var separator = cursor.IndexOf(':');
        if (separator < 1 || !long.TryParse(cursor[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks > DateTime.MaxValue.Ticks)
            throw new ArgumentException($"'{cursor}' is not a cursor the sent log issued.", nameof(cursor));

        var sentAt = new DateTime(ticks, DateTimeKind.Utc);
        var messageId = cursor[(separator + 1)..];

        return Filter.Or(
            Filter.Lt(x => x.SentAt, sentAt),
            Filter.And(Filter.Eq(x => x.SentAt, sentAt), Filter.Gt(x => x.MessageId, messageId)));
    }

    internal static UserMessageEntity ToEntity(UserMessageLogEntry entry) => new()
    {
        MessageId = entry.Id,
        Recipients = new UserMessageRecipientsEntity
        {
            TeamKeys = entry.Recipients?.TeamKeys ?? [],
            AllTeams = entry.Recipients?.AllTeams ?? false,
            UserKeys = entry.Recipients?.UserKeys ?? [],
            Roles = entry.Recipients?.Roles ?? [],
            AccessLevels = [.. (entry.Recipients?.AccessLevels ?? []).Select(x => x.ToString())],
            EveryMember = entry.Recipients?.EveryMember ?? false
        },
        Category = entry.Category,
        Subject = entry.Subject,
        AttachmentCount = entry.AttachmentCount,
        SentByUserKey = entry.SentByUserKey,
        SentByName = entry.SentByName,
        SentAt = entry.SentAt,
        RecipientCount = entry.RecipientCount
    };

    internal static UserMessageLogEntry ToEntry(UserMessageEntity entity) => new()
    {
        Id = entity.MessageId,
        Recipients = new UserMessageRecipients
        {
            TeamKeys = entity.Recipients?.TeamKeys ?? [],
            AllTeams = entity.Recipients?.AllTeams ?? false,
            UserKeys = entity.Recipients?.UserKeys ?? [],
            Roles = entity.Recipients?.Roles ?? [],
            AccessLevels = [.. (entity.Recipients?.AccessLevels ?? []).Select(x => Enum.TryParse<AccessLevel>(x, out var level) ? (AccessLevel?)level : null).OfType<AccessLevel>()],
            EveryMember = entity.Recipients?.EveryMember ?? false
        },
        Category = entity.Category,
        Subject = entity.Subject,
        AttachmentCount = entity.AttachmentCount,
        SentByUserKey = entity.SentByUserKey,
        SentByName = entity.SentByName,
        SentAt = DateTime.SpecifyKind(entity.SentAt, DateTimeKind.Utc),
        RecipientCount = entity.RecipientCount
    };
}
