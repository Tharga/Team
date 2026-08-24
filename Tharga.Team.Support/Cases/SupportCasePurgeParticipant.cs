namespace Tharga.Team.Support.Cases;

/// <summary>
/// Destroys a team's support cases when the team is purged.
/// </summary>
/// <remarks>
/// The store already exposed <c>DeleteCasesForTeamAsync</c> and nothing called it — the support-cases
/// release shipped with that gap documented, because there was no way to reach the store from the purge
/// site. This participant is the wiring that was missing.
/// <para>
/// A case holds whatever a user typed into it, so leaving a purged tenant's cases behind is not only untidy:
/// it is retaining that tenant's free-form content after they have been removed.
/// </para>
/// </remarks>
internal sealed class SupportCasePurgeParticipant(ISupportCaseStore store) : ITeamPurgeParticipant
{
    public string Name => "support cases";

    public Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
        => store.DeleteCasesForTeamAsync(teamKey, cancellationToken);
}
