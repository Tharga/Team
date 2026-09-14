using System.Security.Claims;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Whether the selected team stays selected because a simulation is in force for it.
/// </summary>
/// <remarks>
/// <b>Tharga/Team#276.</b> Team selection used to be re-judged from the principal the simulation had just
/// narrowed. A simulation drops app roles and system scopes, which are exactly what let a non-member see a team
/// reached by consent or <c>teams:read</c> — so the team looked invisible, the caller was moved to one of their
/// own teams, and the simulation followed them there.
/// <para>
/// <b>Nothing here grants access.</b> Keeping a selection only stops the page moving; what the caller can do in
/// the team is still whatever the claims say. And the selection is kept only when claims were actually issued for
/// that team, which happens solely when the caller's real grant resolved there.
/// </para>
/// </remarks>
internal static class SimulatedTeamSelection
{
    /// <summary>
    /// True when a simulation is in force for <paramref name="selectedTeamKey"/> and the caller's real grant
    /// resolved for that team.
    /// </summary>
    /// <param name="principal">The current, possibly narrowed, principal.</param>
    /// <param name="selectedTeamKey">The team the selection cookie names.</param>
    public static bool KeepsSelection(ClaimsPrincipal principal, string selectedTeamKey)
    {
        if (principal == null || string.IsNullOrEmpty(selectedTeamKey)) return false;

        return AccessSimulationCookie.ReadForTeam(principal.FindFirst(AccessSimulationCookie.ClaimType)?.Value, selectedTeamKey) != null
               && principal.HasClaim(TeamClaimTypes.TeamKey, selectedTeamKey);
    }
}
