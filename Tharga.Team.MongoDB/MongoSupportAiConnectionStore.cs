using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// MongoDB implementation of <see cref="ISupportAiConnectionStore"/>: one document per team, replaced whole.
/// </summary>
internal sealed class MongoSupportAiConnectionStore(ISupportAiConnectionCollection collection) : ISupportAiConnectionStore
{
    public async Task<StoredSupportAiConnection> GetAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamKey)) return null;

        var entity = await collection.GetOneAsync(x => x.TeamKey == teamKey);
        return entity == null ? null : ToRecord(entity);
    }

    /// <remarks>
    /// Update first, then insert: the common case is a team changing its mind, and the unique index turns two first
    /// saves at once into one insert and one retried update rather than two documents.
    /// </remarks>
    public async Task SetAsync(StoredSupportAiConnection connection, CancellationToken cancellationToken = default)
    {
        var filter = Builders<SupportAiConnectionEntity>.Filter.Eq(x => x.TeamKey, connection.TeamKey);
        var update = Builders<SupportAiConnectionEntity>.Update
            .Set(x => x.Kind, connection.Kind)
            .Set(x => x.BaseAddress, connection.BaseAddress)
            .Set(x => x.Model, connection.Model)
            .Set(x => x.ProtectedApiKey, connection.ProtectedApiKey)
            .Set(x => x.ChangedAt, connection.ChangedAt)
            .Set(x => x.ChangedByIdentity, connection.ChangedByIdentity)
            .Set(x => x.ChangedByName, connection.ChangedByName);

        if ((await collection.UpdateOneAsync(filter, update))?.Before != null) return;

        try
        {
            await collection.AddAsync(ToEntity(connection));
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await collection.UpdateOneAsync(filter, update);
        }
    }

    public async Task<bool> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(x => x.TeamKey == teamKey);
        if (entity == null) return false;

        await collection.DeleteOneAsync(entity.Id);
        return true;
    }

    private static StoredSupportAiConnection ToRecord(SupportAiConnectionEntity entity) => new()
    {
        TeamKey = entity.TeamKey,
        Kind = entity.Kind,
        BaseAddress = entity.BaseAddress,
        Model = entity.Model,
        ProtectedApiKey = entity.ProtectedApiKey,
        ChangedAt = entity.ChangedAt,
        ChangedByIdentity = entity.ChangedByIdentity,
        ChangedByName = entity.ChangedByName
    };

    private static SupportAiConnectionEntity ToEntity(StoredSupportAiConnection record) => new()
    {
        TeamKey = record.TeamKey,
        Kind = record.Kind,
        BaseAddress = record.BaseAddress,
        Model = record.Model,
        ProtectedApiKey = record.ProtectedApiKey,
        ChangedAt = record.ChangedAt,
        ChangedByIdentity = record.ChangedByIdentity,
        ChangedByName = record.ChangedByName
    };
}
