using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// MongoDB implementation of <see cref="ISupportMailPositionStore"/>.
/// </summary>
/// <remarks>
/// <b>Nothing here fails loudly, and that is the design rather than laziness.</b> The position is a bookmark:
/// losing it costs a re-read of the mailbox, and <see cref="ISupportEventLedger"/> then recognises everything
/// already handled. Letting a write failure escape would stop a poll that had already succeeded, which turns
/// a cheap re-read into a stalled mailbox.
/// <para>
/// <b>Stored as <c>long</c> because BSON has no unsigned integer.</b> A UID is a 32-bit unsigned value, so it
/// fits a signed 64-bit field exactly and comes back unchanged. Storing it as <c>int</c> would have wrapped
/// on a mailbox that has seen more than two billion messages, which is a real number for a shared support
/// address that has run for years.
/// </para>
/// </remarks>
internal sealed class MongoSupportMailPositionStore(
    ISupportMailPositionCollection collection,
    TimeProvider timeProvider,
    ILogger<MongoSupportMailPositionStore> logger = null) : ISupportMailPositionStore
{
    public async Task<SupportMailPosition> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var entity = await collection.GetOneAsync(x => x.Key == key);

            return entity == null
                ? SupportMailPosition.Start
                : new SupportMailPosition((uint)entity.UidValidity, (uint)entity.LastUid);
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The support mailbox position could not be read. The mailbox will be re-read from the start.");

            return SupportMailPosition.Start;
        }
    }

    public async Task SetAsync(string key, SupportMailPosition position, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportMailPositionEntity>.Filter.Eq(x => x.Key, key);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            var update = Builders<SupportMailPositionEntity>.Update
                .Set(x => x.UidValidity, position.UidValidity)
                .Set(x => x.LastUid, position.LastUid)
                .Set(x => x.UpdatedAt, now);

            var result = await collection.UpdateOneAsync(filter, update);

            if (result?.Before != null) return;

            await collection.AddAsync(new SupportMailPositionEntity
            {
                Key = key,
                UidValidity = position.UidValidity,
                LastUid = position.LastUid,
                UpdatedAt = now
            });
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Two instances polling for the first time together: the unique index lets exactly one insert,
            // and the loser has nothing to do -- the position it would have written is the one already
            // there, or the next poll writes it.
            logger?.LogDebug("Another instance recorded the mailbox position first.");
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The support mailbox position could not be recorded. The mail just handled may be re-read, which the event ledger makes harmless.");
        }
    }
}
