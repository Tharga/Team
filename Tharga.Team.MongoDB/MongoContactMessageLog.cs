using System.Globalization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IContactMessageLog"/>, paged newest first by a cursor of send time and id, a page read without the addresses.</summary>
internal sealed class MongoContactMessageLog(IContactMessageCollection collection) : IContactMessageLog
{
    internal const string AddresseesField = nameof(ContactMessageEntity.Addressees);

    private static readonly FilterDefinitionBuilder<ContactMessageEntity> Filter = Builders<ContactMessageEntity>.Filter;

    public Task AddAsync(ContactMessageLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return collection.AddAsync(ToEntity(entry));
    }

    public async Task<ContactMessageLogEntry> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var entity = await collection.GetOneAsync(x => x.MessageId == id, cancellationToken: cancellationToken);

        return entity == null ? null : ToEntry(entity);
    }

    /// <remarks>The addresses are left on the server: a message may list thousands, and a page of the log names none of them.</remarks>
    public async Task<ContactMessageLogPage> ListAsync(string cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(pageSize, 1, 500);
        var result = await collection.GetManyAsync(After(cursor), PageOptions(size), cancellationToken);
        var found = (result?.Items ?? []).ToArray();
        var page = found.Take(size).ToArray();

        return new ContactMessageLogPage
        {
            Items = [.. page.Select(ToEntry)],
            NextCursor = found.Length > size ? CursorOf(page[^1]) : null
        };
    }

    internal static Options<ContactMessageEntity> PageOptions(int size) => new()
    {
        Sort = Builders<ContactMessageEntity>.Sort.Descending(x => x.SentAt).Ascending(x => x.MessageId),
        Limit = size + 1,
        Projection = Builders<ContactMessageEntity>.Projection.Exclude(AddresseesField)
    };

    internal static string CursorOf(ContactMessageEntity entity) => $"{entity.SentAt.Ticks.ToString(CultureInfo.InvariantCulture)}:{entity.MessageId}";

    /// <exception cref="ArgumentException">The cursor was not issued by this log.</exception>
    internal static FilterDefinition<ContactMessageEntity> After(string cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return Filter.Empty;

        var separator = cursor.IndexOf(':');
        if (separator < 1 || !long.TryParse(cursor[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks > DateTime.MaxValue.Ticks)
            throw new ArgumentException($"'{cursor}' is not a cursor the contact message log issued.", nameof(cursor));

        var sentAt = new DateTime(ticks, DateTimeKind.Utc);
        var messageId = cursor[(separator + 1)..];

        return Filter.Or(
            Filter.Lt(x => x.SentAt, sentAt),
            Filter.And(Filter.Eq(x => x.SentAt, sentAt), Filter.Gt(x => x.MessageId, messageId)));
    }

    internal static ContactMessageEntity ToEntity(ContactMessageLogEntry entry) => new()
    {
        MessageId = entry.Id,
        Category = entry.Category,
        Subject = entry.Subject,
        AttachmentCount = entry.AttachmentCount,
        SentByUserKey = entry.SentByUserKey,
        SentByName = entry.SentByName,
        SentAt = entry.SentAt,
        RecipientCount = entry.RecipientCount,
        OptedOutCount = entry.OptedOutCount,
        LeftOutCount = entry.LeftOutCount,
        Addressees = [.. (entry.Addressees ?? []).Where(x => x != null).Select(x => new ContactMessageAddresseeEntity
        {
            Address = x.Address,
            Name = x.Name,
            Detail = x.Detail,
            References = x.References ?? [],
            Status = x.Status,
            OptedOutOfAll = x.OptedOutOfAll,
            InvalidAddress = x.InvalidAddress,
            ExcludedReason = x.ExcludedReason
        })]
    };

    internal static ContactMessageLogEntry ToEntry(ContactMessageEntity entity) => new()
    {
        Id = entity.MessageId,
        Category = entity.Category,
        Subject = entity.Subject,
        AttachmentCount = entity.AttachmentCount,
        SentByUserKey = entity.SentByUserKey,
        SentByName = entity.SentByName,
        SentAt = DateTime.SpecifyKind(entity.SentAt, DateTimeKind.Utc),
        RecipientCount = entity.RecipientCount,
        OptedOutCount = entity.OptedOutCount,
        LeftOutCount = entity.LeftOutCount,
        Addressees = [.. (entity.Addressees ?? []).Select(x => new ContactAddresseeCheck
        {
            Address = x.Address,
            Name = x.Name,
            Detail = x.Detail,
            References = x.References ?? [],
            Status = x.Status,
            OptedOutOfAll = x.OptedOutOfAll,
            InvalidAddress = x.InvalidAddress,
            ExcludedReason = x.ExcludedReason
        })]
    };
}
