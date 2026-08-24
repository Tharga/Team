using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Destroys a team's stored icons when the team is purged.
/// </summary>
/// <remarks>
/// Icons are owner-keyed rather than content-addressed — <see cref="IconEntity"/> carries a
/// <c>Kind</c> and an <c>OwnerKey</c> — so a team's icons are identifiable and deleting them by team is
/// correct. This is hygiene rather than security: an orphaned icon grants nothing. It is included because a
/// purge that leaves blobs behind is the same class of omission as one that leaves credentials behind, and
/// one seam should cover every per-team store rather than the two somebody remembered.
/// <para>
/// Only <see cref="IconKind.Team"/> is removed. A user's icon belongs to the user, who may be a member of
/// other teams and outlives this one.
/// </para>
/// </remarks>
internal sealed class IconPurgeParticipant(IIconRepositoryCollection collection) : ITeamPurgeParticipant
{
    public string Name => "icons";

    public async Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var filter = Builders<IconEntity>.Filter.And(
            Builders<IconEntity>.Filter.Eq(x => x.Kind, IconKind.Team),
            Builders<IconEntity>.Filter.Eq(x => x.OwnerKey, teamKey));

        var removed = 0;
        await foreach (var entity in collection.GetAsync(filter).WithCancellation(cancellationToken))
        {
            await collection.DeleteOneAsync(Builders<IconEntity>.Filter.Eq(x => x.Key, entity.Key));
            removed++;
        }

        return removed;
    }
}
