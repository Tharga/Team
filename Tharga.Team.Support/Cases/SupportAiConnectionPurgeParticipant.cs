namespace Tharga.Team.Support.Cases;

/// <summary>
/// Removes a team's AI connection when the team is purged.
/// </summary>
/// <remarks>
/// The record holds the team's key for a provider, encrypted or not. A purged team's key left behind is a credential
/// kept for an account that no longer exists here.
/// </remarks>
internal sealed class SupportAiConnectionPurgeParticipant(ISupportAiConnectionStore store) : ITeamPurgeParticipant
{
    public string Name => "support assistant connection";

    public async Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
        => await store.DeleteForTeamAsync(teamKey, cancellationToken) ? 1 : 0;
}
