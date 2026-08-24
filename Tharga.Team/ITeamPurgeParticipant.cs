namespace Tharga.Team;

/// <summary>
/// A store holding data for a team, which must be destroyed when that team is purged.
/// </summary>
/// <remarks>
/// <b>Purging a team does not reach the toolkit's own collections on its own.</b> It deletes the team record
/// and drops the *host's* per-team database — but the toolkit's stores (API keys, icons, support cases) are
/// shared collections keyed by team, so nothing there is touched. Without a participant, a purged tenant's
/// data outlives it. For API keys that is not merely untidy: a credential outliving the tenant it authorized
/// is a security problem, and worse if the team key is later reused.
/// <para>
/// <b>Register one per store rather than teaching purge about each.</b> The alternative — having the team
/// service call every store directly — has nowhere to resolve them from: the purge site is a base class a
/// host derives from, and adding a constructor parameter for each store repeats the pattern that already
/// silently disables a feature when a subclass forgets to forward it.
/// </para>
/// <para>
/// <b>Participants run before the team record is deleted.</b> The writes cannot be made atomic, so the
/// failure direction is chosen: a participant that throws aborts the purge with the team still present, which
/// is visible and can be purged again. Deleting the team first and then failing would leave data that nothing
/// can find or clean up.
/// </para>
/// </remarks>
public interface ITeamPurgeParticipant
{
    /// <summary>What this participant removes, for the log and for the error when it fails.</summary>
    string Name { get; }

    /// <summary>
    /// Destroys everything this store holds for the team. Returns how many records were removed.
    /// </summary>
    /// <remarks>
    /// Must be safe to run again: a purge that failed part-way is retried, so a participant that has already
    /// run should remove nothing and report zero rather than failing.
    /// </remarks>
    Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default);
}
