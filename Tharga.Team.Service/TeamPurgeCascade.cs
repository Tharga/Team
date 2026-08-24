using Microsoft.Extensions.Logging;

namespace Tharga.Team.Service;

/// <summary>
/// Destroys a team's data in the toolkit's own stores, before the team itself is purged.
/// </summary>
/// <remarks>
/// <b>Purge does not reach these stores on its own.</b> It deletes the team record and drops the *host's*
/// per-team database — but API keys, icons and support cases live in the toolkit's own shared collections
/// keyed by team, so nothing there is touched. For API keys that is a security problem rather than untidiness:
/// a credential outliving the tenant it authorized, and inherited by whoever next takes that team key.
/// <para>
/// <b>A collaborator, not a decorator.</b> It is invoked from the one place that already intercepts purge —
/// the authorization decorator — rather than wrapping <see cref="ITeamService"/> again, which would mean
/// thirty pass-through members for one intercepted call. It authorizes nothing; by the time it runs,
/// <c>teams:purge</c> has been checked.
/// </para>
/// <para>
/// <b>Soft delete deliberately does not cascade.</b> A soft-deleted team can be restored, and a restore that
/// brought back a team with no API keys, no icons and no history would be a restore in name only. Purge is
/// the irreversible operation, so purge is where data is destroyed.
/// </para>
/// </remarks>
public sealed class TeamPurgeCascade(
    IEnumerable<ITeamPurgeParticipant> participants,
    ILogger<TeamPurgeCascade> logger = null)
{
    /// <summary>
    /// Runs every participant. Throws if one fails, leaving the team present so the purge can be retried.
    /// </summary>
    /// <remarks>
    /// <b>The failure direction is chosen, not accidental.</b> The participant writes and the team-record
    /// delete cannot be made atomic. Aborting first leaves a team whose data is partly gone — visible, and
    /// re-purgeable, because participants are required to be safe to run again. Deleting the team first and
    /// then failing would leave data nothing can find, which is the outcome this whole seam exists to
    /// prevent.
    /// </remarks>
    public async Task RunAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        foreach (var participant in participants)
        {
            try
            {
                var removed = await participant.PurgeTeamDataAsync(teamKey, cancellationToken);

                if (removed > 0)
                    logger?.LogInformation("Purging team {TeamKey} removed {Count} record(s) from {Participant}.", teamKey, removed, participant.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Purging team '{teamKey}' failed while removing {participant.Name}. The team has not been " +
                    "deleted, and the purge can be retried.", ex);
            }
        }
    }
}
