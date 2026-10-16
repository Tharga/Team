using Microsoft.Extensions.Logging;

namespace Tharga.Team.Service;

/// <summary>
/// Runs every <see cref="ITeamMemberRemovalParticipant"/> after someone stops being a member of a team.
/// </summary>
/// <remarks>
/// <b>A collaborator, not a decorator</b>, like <see cref="TeamPurgeCascade"/>: invoked from the authorization
/// decorator, which already intercepts every removal path, rather than wrapping <see cref="ITeamService"/>
/// again for three calls.
/// <para>
/// <b>Never throws.</b> The removal has already happened by the time this runs, so a failure here cannot be
/// reported as a failed removal — that would tell the caller something untrue. It is logged instead.
/// </para>
/// </remarks>
public sealed class TeamMemberRemovalCascade(
    IEnumerable<ITeamMemberRemovalParticipant> participants,
    IUserService userService = null,
    ILogger<TeamMemberRemovalCascade> logger = null)
{
    /// <summary>Cleans up after <paramref name="userKey"/> stopped being a member of each of <paramref name="teamKeys"/>.</summary>
    public async Task RunAsync(IEnumerable<string> teamKeys, string userKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userKey)) return;

        foreach (var teamKey in teamKeys.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal))
        {
            foreach (var participant in participants)
            {
                try
                {
                    var affected = await participant.MemberRemovedAsync(teamKey, userKey, cancellationToken);
                    if (affected > 0)
                        logger?.LogInformation("Removing user {UserKey} from team {TeamKey} cleaned up {Count} record(s) in {Participant}.", userKey, teamKey, affected, participant.Name);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Removing user {UserKey} from team {TeamKey} could not clean up {Participant}. The removal stands.", userKey, teamKey, participant.Name);
                }
            }
        }
    }

    /// <summary>Cleans up after <paramref name="userKey"/> stopped being a member of <paramref name="teamKey"/>.</summary>
    public Task RunAsync(string teamKey, string userKey, CancellationToken cancellationToken = default)
        => RunAsync([teamKey], userKey, cancellationToken);

    /// <summary>Cleans up after the current user left <paramref name="teamKey"/>.</summary>
    public async Task RunForCurrentUserAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (userService == null) return;

        try
        {
            var user = await userService.GetCurrentUserAsync();
            await RunAsync(teamKey, user?.Key, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Leaving team {TeamKey} could not resolve the current user to clean up after. The removal stands.", teamKey);
        }
    }

    /// <summary>
    /// The teams <paramref name="userKey"/> is a member of, read <b>before</b> removing them from all of them,
    /// since afterwards there is nothing left to read. Empty, not a failure, when the store cannot answer.
    /// </summary>
    public async Task<string[]> GetTeamKeysAsync(ITeamService teams, string userKey)
    {
        var keys = new List<string>();
        foreach (var level in Enum.GetValues<AccessLevel>())
        {
            try
            {
                keys.AddRange((await teams.GetTeamsForUserWithAccessLevelAsync(userKey, level) ?? []).Select(x => x.Key));
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not list the teams of user {UserKey} at {AccessLevel} before removing them; their data in those teams is not cleaned up.", userKey, level);
            }
        }

        return keys.Distinct(StringComparer.Ordinal).ToArray();
    }
}
