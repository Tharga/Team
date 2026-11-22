using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// MongoDB implementation of <see cref="ISupportAssistantLimitStore"/>: one document per team that has a limit of its own.
/// </summary>
internal sealed class MongoSupportAssistantLimitStore(ISupportAssistantLimitCollection collection) : ISupportAssistantLimitStore
{
    public async Task<StoredSupportAssistantLimit> GetAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamKey)) return null;

        var entity = await collection.GetOneAsync(x => x.TeamKey == teamKey);
        return entity == null ? null : ToRecord(entity);
    }

    /// <remarks>Update first, then insert, as the AI connection does: two first saves at once become one insert and one retried update.</remarks>
    public async Task SetAsync(StoredSupportAssistantLimit limit, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportAssistantLimitEntity>.Filter.Eq(x => x.TeamKey, limit.TeamKey);
        var update = Builders<SupportAssistantLimitEntity>.Update
            .Set(x => x.Limit, limit.Limit)
            .Set(x => x.ChangedAt, limit.ChangedAt)
            .Set(x => x.ChangedByIdentity, limit.ChangedByIdentity)
            .Set(x => x.ChangedByName, limit.ChangedByName);

        if ((await collection.UpdateOneAsync(filter, update))?.Before != null) return;

        if (await collection.TryAddAsync(ToEntity(limit))) return;

        if ((await collection.UpdateOneAsync(filter, update))?.Before == null)
            throw new InvalidOperationException($"The support assistant limit of team '{limit.TeamKey}' could not be saved.");
    }

    public async Task<bool> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamKey)) return false;

        var entity = await collection.GetOneAsync(x => x.TeamKey == teamKey);
        if (entity == null) return false;

        await collection.DeleteOneAsync(entity.Id);
        return true;
    }

    /// <remarks>Every document: one per team that has a limit of its own, and the default. Few by nature, since a limit is an exception staff made.</remarks>
    public async Task<StoredSupportAssistantLimit[]> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var found = await collection.GetManyAsync(Builders<SupportAssistantLimitEntity>.Filter.Empty, new Options<SupportAssistantLimitEntity> { Limit = 5000 }, cancellationToken);
        return [.. (found?.Items ?? []).Select(ToRecord)];
    }

    internal static StoredSupportAssistantLimit ToRecord(SupportAssistantLimitEntity entity) => new()
    {
        TeamKey = entity.TeamKey,
        Limit = entity.Limit,
        ChangedAt = entity.ChangedAt,
        ChangedByIdentity = entity.ChangedByIdentity,
        ChangedByName = entity.ChangedByName
    };

    internal static SupportAssistantLimitEntity ToEntity(StoredSupportAssistantLimit record) => new()
    {
        TeamKey = record.TeamKey,
        Limit = record.Limit,
        ChangedAt = record.ChangedAt,
        ChangedByIdentity = record.ChangedByIdentity,
        ChangedByName = record.ChangedByName
    };
}
