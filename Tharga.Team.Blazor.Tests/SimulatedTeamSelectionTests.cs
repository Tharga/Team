using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// While a simulation is in force for the selected team, that selection stands (Tharga/Team#276).
/// </summary>
/// <remarks>
/// The selection used to be re-judged from the narrowed principal. A simulation drops app roles and system
/// scopes — exactly what let a non-member see a consented team — so the team looked invisible and the caller
/// was moved to a fallback. The decision must rest on the caller's <b>real</b> access instead, and
/// <see cref="TeamClaimTypes.TeamKey"/> is that: it is issued only when the real grant resolved, and the filter
/// never removes it.
/// </remarks>
public class SimulatedTeamSelectionTests
{
    private const string TeamA = "team-a";

    private static ClaimsPrincipal Principal(string selected, string simulationTeam, string grantedTeam)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "alice") };
        if (selected != null) claims.Add(new Claim(Constants.TeamKeyCookie, selected));
        if (grantedTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, grantedTeam));
        if (simulationTeam != null)
        {
            claims.Add(new Claim(AccessSimulationCookie.ClaimType, AccessSimulationCookie.Write(new AccessSimulation
            {
                Kind = AccessSimulationKind.Scopes,
                Label = "test",
                TeamKey = simulationTeam
            })));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void KeepsTheSelection_WhenSimulatingThatTeamWithRealAccessToIt()
        => Assert.True(SimulatedTeamSelection.KeepsSelection(Principal(TeamA, TeamA, TeamA), TeamA));

    [Fact]
    public void DoesNotKeepIt_WithoutASimulation()
        => Assert.False(SimulatedTeamSelection.KeepsSelection(Principal(TeamA, simulationTeam: null, TeamA), TeamA));

    [Fact]
    public void DoesNotKeepIt_WhenTheSimulationBelongsToAnotherTeam()
        => Assert.False(SimulatedTeamSelection.KeepsSelection(Principal(TeamA, "team-b", TeamA), TeamA));

    /// <summary>
    /// No team claims were issued for the selection, so the caller's real grant did not resolve there. A
    /// simulation carries no access of its own and must not hold a team the caller cannot reach.
    /// </summary>
    [Fact]
    public void DoesNotKeepIt_WhenClaimsWereNotIssuedForThatTeam()
        => Assert.False(SimulatedTeamSelection.KeepsSelection(Principal(TeamA, TeamA, grantedTeam: null), TeamA));

    [Fact]
    public void DoesNotKeepIt_WhenClaimsWereIssuedForADifferentTeam()
        => Assert.False(SimulatedTeamSelection.KeepsSelection(Principal(TeamA, TeamA, "team-b"), TeamA));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DoesNotKeepIt_WithNoSelectedTeam(string selected)
        => Assert.False(SimulatedTeamSelection.KeepsSelection(Principal(TeamA, TeamA, TeamA), selected));

    [Fact]
    public void DoesNotKeepIt_ForNoPrincipal()
        => Assert.False(SimulatedTeamSelection.KeepsSelection(null, TeamA));
}
