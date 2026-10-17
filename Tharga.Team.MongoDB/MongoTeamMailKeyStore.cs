using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// <see cref="ITeamMailKeyStore"/> on MongoDB: one document per team, every change a single conditional write.
/// </summary>
internal sealed class MongoTeamMailKeyStore(ITeamMailKeysCollection collection) : ITeamMailKeyStore
{
    public async Task<TeamMailKeyRecord[]> GetForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(x => x.TeamKey == teamKey);
        return entity == null ? [] : [.. entity.Keys.Select(x => ToRecord(entity.TeamKey, x))];
    }

    public async Task<TeamMailKeyRecord> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(Builders<TeamMailKeysEntity>.Filter.ElemMatch(x => x.Keys, k => k.Key == key));
        var entry = entity?.Keys.FirstOrDefault(x => x.Key == key);
        return entry == null ? null : ToRecord(entity.TeamKey, entry);
    }

    public async Task<bool> IssueAsync(TeamMailKeyRecord primary, CancellationToken cancellationToken = default)
    {
        try
        {
            await collection.AddAsync(new TeamMailKeysEntity { TeamKey = primary.TeamKey, Keys = [ToEntry(primary)], Revision = 1 });
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task RotateAsync(string teamKey, TeamMailKeyRecord newPrimary, DateTime retiresAt, DateTime now, CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(x => x.TeamKey == teamKey) ?? throw new InvalidOperationException($"Team {teamKey} has no mail key to rotate.");

        var filter = Builders<TeamMailKeysEntity>.Filter.And(
            Builders<TeamMailKeysEntity>.Filter.Eq(x => x.TeamKey, teamKey),
            Builders<TeamMailKeysEntity>.Filter.Eq(x => x.Revision, entity.Revision));

        var update = Builders<TeamMailKeysEntity>.Update
            .Set(x => x.Keys, TeamMailKeyRotation.Rotate(entity.Keys, ToEntry(newPrimary), retiresAt, now))
            .Set(x => x.Revision, entity.Revision + 1);

        var result = await collection.UpdateOneAsync(filter, update);
        if (result?.Before == null) throw new InvalidOperationException($"The mail keys of team {teamKey} changed while they were being rotated; nothing was rotated.");
    }

    public async Task DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(x => x.TeamKey == teamKey);
        if (entity != null) await collection.DeleteOneAsync(entity.Id);
    }

    private static TeamMailKeyRecord ToRecord(string teamKey, TeamMailKeyEntry entry) =>
        new() { TeamKey = teamKey, Key = entry.Key, Role = entry.Role, CreatedAt = entry.CreatedAt, RetiresAt = entry.RetiresAt };

    private static TeamMailKeyEntry ToEntry(TeamMailKeyRecord record) =>
        new() { Key = record.Key, Role = record.Role, CreatedAt = record.CreatedAt, RetiresAt = record.RetiresAt };
}
