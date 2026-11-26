using Tharga.Team.MongoDB;
using Tharga.Team.Sample.Framework.Team;

namespace Tharga.Team.Sample.Framework;

/// <summary>
/// Demonstrates <see cref="ISessionPolicy"/>, the host's numbers for how long a session may last in a team (Tharga/Team#471). Driven by the team's name, so
/// renaming a team tries it: in a team named <c>Strict</c> a session ends after two minutes without use, and a person may have one session there at a time,
/// the newest sign-in winning. Every other team has no limits, so the sample works as it did for them.
/// </summary>
/// <remarks>
/// <b>It reads the team from the repository, not through <c>ITeamService</c></b>: the policy is asked while the cookie is being validated, before the request
/// has a principal, and the authorizing service would refuse an anonymous caller. A real application decides from its own records, not from a team's name.
/// Two minutes is short for the sample's sake: leave a tab alone and after a minute it asks "Are you still there?" (the sample's 20-second activity interval
/// leaves room for the one-minute warning); answer nothing and every tab lands on <c>/session-ended</c> a minute later. Sign in to a Strict team in a second
/// browser and the first one's open tabs go there at once.
/// </remarks>
public sealed class SampleSessionPolicy(ITeamRepository<TeamEntity, TeamMember> teams) : ISessionPolicy
{
    private const string StrictTeamName = "Strict";

    private static readonly SessionLimits Strict = new() { IdleTimeout = TimeSpan.FromMinutes(2), SingleSession = true };

    public async Task<SessionLimits> GetLimitsAsync(UserSession session, string teamKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(teamKey)) return SessionLimits.None;

        var team = await teams.GetAsync(teamKey);
        return string.Equals(team?.Name, StrictTeamName, StringComparison.OrdinalIgnoreCase) ? Strict : SessionLimits.None;
    }
}
