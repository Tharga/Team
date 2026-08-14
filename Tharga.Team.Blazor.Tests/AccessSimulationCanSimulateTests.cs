using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Answering "may this caller simulate?" from the claims the caller already carries, instead of resolving
/// the grant again. Tharga/Team#219.
/// </summary>
/// <remarks>
/// <b>This is not a second place deciding.</b> <see cref="TeamMembershipClaimsBuilder"/> builds the scope
/// claims by calling the same <c>TeamGrantResolver.ResolveAsync</c> with the same arguments and emitting
/// every <c>grant.Scopes</c> entry as a <see cref="TeamClaimTypes.Scope"/> claim, and
/// <c>AccessSimulationFilter</c> only ever removes claims. The claim is therefore the resolver's own answer,
/// already carried on the principal — reading it is a cache hit, not a restatement of the rule.
/// <para>
/// <b>Two conditions have to hold before the claims can be trusted</b>, and both are load-bearing:
/// no simulation may be active (the filter removes scope claims, so a filtered principal cannot say what
/// the caller really holds), and the principal must carry a <see cref="TeamClaimTypes.TeamKey"/> claim for
/// the team currently selected (which is issued only when the builder resolved a grant, so its presence is
/// what separates "holds no scopes" from "claims were never issued"). Otherwise the grant is resolved as
/// before.
/// </para>
/// <para>
/// The freshness this trades away is the same freshness every other scope-gated surface in the toolkit
/// already lives with: a grant changed mid-session reaches the principal at the next claim revalidation.
/// </para>
/// </remarks>
public class AccessSimulationCanSimulateTests
{
    private const string TeamKey = "team-1";
    private const string OtherTeamKey = "team-2";
    private const string UserKey = "user-1";

    private sealed class FakeAuthStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private static ClaimsPrincipal Principal(
        string selectedTeam = TeamKey,
        string claimsIssuedForTeam = TeamKey,
        string[] scopes = null,
        AccessSimulation simulation = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, UserKey) };

        if (selectedTeam != null) claims.Add(new Claim(Constants.TeamKeyCookie, selectedTeam));
        if (claimsIssuedForTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, claimsIssuedForTeam));

        foreach (var scope in scopes ?? []) claims.Add(new Claim(TeamClaimTypes.Scope, scope));

        if (simulation != null)
            claims.Add(new Claim(AccessSimulationCookie.ClaimType, AccessSimulationCookie.Write(simulation)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>
    /// The team service is the round trip being removed, so the tests assert on whether it was touched at
    /// all — a faster answer that still queries has not fixed what was reported.
    /// </summary>
    private static (AccessSimulationState State, Mock<ITeamService> TeamService) Build(
        ClaimsPrincipal principal, params string[] realScopes)
    {
        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamMemberAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Mock.Of<ITeamMember>(m => m.Key == "member-1" && m.AccessLevel == AccessLevel.Administrator));

        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));

        var scopeRegistry = new Mock<IScopeRegistry>();
        scopeRegistry.Setup(x => x.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(realScopes);

        var options = new ThargaBlazorOptions();
        options.Simulation.Enabled = true;

        var state = new AccessSimulationState(
            new FakeAuthStateProvider(principal),
            teamService.Object,
            userService.Object,
            navigationManager: null,
            jsRuntime: null,
            Options.Create(options),
            scopeRegistry.Object);

        return (state, teamService);
    }

    private static AccessSimulation Demo() => new()
    {
        Kind = AccessSimulationKind.Scopes,
        Label = "Demo mode",
        Scopes = ["orders:read"]
    };

    // --- the fast path ---

    [Fact]
    public async Task WithTheScopeOnTheirClaims_TheyCanSimulateWithoutAnyLookup()
    {
        var (state, teamService) = Build(Principal(scopes: [SimulationScopes.Simulate, "orders:read"]));

        Assert.True(await state.CanSimulateAsync());
        teamService.Verify(x => x.GetTeamMemberAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>The self-check: the same free answer has to be able to say no.</summary>
    [Fact]
    public async Task WithoutTheScopeOnTheirClaims_TheyCannotSimulateAndStillNothingIsLookedUp()
    {
        var (state, teamService) = Build(Principal(scopes: ["orders:read"]));

        Assert.False(await state.CanSimulateAsync());
        teamService.Verify(x => x.GetTeamMemberAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // --- when the claims cannot answer ---

    /// <summary>
    /// The case the fast path must never take. A simulation removes scope claims, so a caller who simulated
    /// <c>simulation:use</c> away would be told they cannot simulate — and could not reach their own picker
    /// to undo it.
    /// </summary>
    [Fact]
    public async Task WhileSimulating_TheRealGrantIsResolvedEvenThoughTheClaimIsGone()
    {
        var principal = Principal(scopes: ["orders:read"], simulation: Demo());
        var (state, teamService) = Build(principal, SimulationScopes.Simulate, "orders:read");

        Assert.True(await state.CanSimulateAsync());
        teamService.Verify(x => x.GetTeamMemberAsync(TeamKey, UserKey), Times.Once);
    }

    /// <summary>
    /// No <see cref="TeamClaimTypes.TeamKey"/> claim means the builder never issued claims for this team,
    /// so an absent scope claim means nothing and the grant has to be resolved. This is what keeps the
    /// consent-reaching caller of <see cref="AccessSimulationConsentAccessTests"/> working.
    /// </summary>
    [Fact]
    public async Task WithNoIssuedClaims_TheGrantIsResolved()
    {
        var principal = Principal(claimsIssuedForTeam: null);
        var (state, teamService) = Build(principal, SimulationScopes.Simulate);

        Assert.True(await state.CanSimulateAsync());
        teamService.Verify(x => x.GetTeamMemberAsync(TeamKey, UserKey), Times.Once);
    }

    /// <summary>
    /// Claims issued for a different team than the one selected describe the wrong team, which is the state
    /// between choosing a team and the reload that re-issues them.
    /// </summary>
    [Fact]
    public async Task WithClaimsIssuedForAnotherTeam_TheGrantIsResolved()
    {
        var principal = Principal(selectedTeam: TeamKey, claimsIssuedForTeam: OtherTeamKey);
        var (state, teamService) = Build(principal, SimulationScopes.Simulate);

        Assert.True(await state.CanSimulateAsync());
        teamService.Verify(x => x.GetTeamMemberAsync(TeamKey, UserKey), Times.Once);
    }

    // --- the two paths must not disagree ---

    /// <summary>
    /// The property that makes the fast path safe: for the same caller, reading the claim and resolving the
    /// grant give the same answer. Asserted both ways round so neither a stuck true nor a stuck false
    /// passes.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothPathsAgreeForTheSameCaller(bool holdsTheScope)
    {
        string[] real = holdsTheScope ? [SimulationScopes.Simulate, "orders:read"] : ["orders:read"];

        var (fromClaims, _) = Build(Principal(scopes: real), real);
        var (fromGrant, _) = Build(Principal(claimsIssuedForTeam: null), real);

        Assert.Equal(holdsTheScope, await fromClaims.CanSimulateAsync());
        Assert.Equal(holdsTheScope, await fromGrant.CanSimulateAsync());
    }

    [Fact]
    public async Task WithTheFeatureOff_NobodyCanSimulateAndNothingIsLookedUp()
    {
        var teamService = new Mock<ITeamService>();

        var state = new AccessSimulationState(
            new FakeAuthStateProvider(Principal(scopes: [SimulationScopes.Simulate])),
            teamService.Object,
            Mock.Of<IUserService>(),
            navigationManager: null,
            jsRuntime: null,
            Options.Create(new ThargaBlazorOptions()));

        Assert.False(await state.CanSimulateAsync());
        teamService.VerifyNoOtherCalls();
    }
}
