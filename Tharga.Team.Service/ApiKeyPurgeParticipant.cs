using MongoDB.Driver;

namespace Tharga.Team.Service;

/// <summary>
/// Destroys a team's API keys when the team is purged.
/// </summary>
/// <remarks>
/// <b>The one participant that is a security fix rather than tidiness.</b> An API key is a credential; left
/// behind by a purge it outlives the tenant that authorized it, and if the team key is later reused the new
/// tenant inherits it. Authentication refuses a key whose team is missing, which closes the deleted-team
/// case — but a *reused* key names a team that exists again, so only destroying the keys closes that one.
/// <para>
/// Removed one by one rather than as a bulk delete so each removal goes through the same path as any other
/// key deletion. A purged team's key count is small; this is not a hot path.
/// </para>
/// </remarks>
internal sealed class ApiKeyPurgeParticipant(IApiKeyRepositoryCollection collection) : ITeamPurgeParticipant
{
    public string Name => "API keys";

    public async Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ApiKeyEntity>.Filter.Eq(x => x.TeamKey, teamKey);

        var removed = 0;
        await foreach (var entity in collection.GetAsync(filter).WithCancellation(cancellationToken))
        {
            await collection.DeleteOneAsync(Builders<ApiKeyEntity>.Filter.Eq(x => x.Key, entity.Key));
            removed++;
        }

        return removed;
    }
}
