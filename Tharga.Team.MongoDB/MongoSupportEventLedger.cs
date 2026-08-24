using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// MongoDB implementation of <see cref="ISupportEventLedger"/>.
/// </summary>
/// <remarks>
/// <b>The insert is the decision.</b> A unique index on source and event id means the write either succeeds -
/// this instance is the one that handles the event - or fails as a duplicate, meaning somebody already did.
/// That is atomic across instances, which a read followed by a write is not: two instances handed the same
/// Slack retry would both read "not seen" and both append the reply.
/// </remarks>
internal sealed class MongoSupportEventLedger(ISupportEventLedgerCollection collection, TimeProvider timeProvider) : ISupportEventLedger
{
    public async Task<bool> TryRecordAsync(string source, string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(eventId)) return true;

        try
        {
            await collection.AddAsync(new SupportEventLedgerEntity
            {
                Source = source,
                EventId = eventId,
                HandledAt = timeProvider.GetUtcNow().UtcDateTime
            });

            return true;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
