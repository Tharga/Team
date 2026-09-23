using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A caller who reaches a team through consent rather than membership can still simulate.
/// </summary>
/// <remarks>
/// <b>Reported from the sample, 2026-08-03:</b> a Developer holding Administrator on a team by consent
/// saw no simulation control at all. <see cref="AccessSimulationState"/> resolved the caller's grant with
/// a membership lookup, which returns null for a non-member, so the feature was invisible to exactly the
/// people it was built for.
/// <para>
/// The fix was not to add a consent branch — it was to stop having a branch. Grant resolution belongs to
/// <c>TeamGrantResolver</c>, which already answers both cases and is what issues the caller's claims, so
/// asking it means the picker and the principal cannot disagree about what someone holds. A second place
/// restating that rule is the defect this codebase keeps paying for.
/// </para>
/// </remarks>
public class AccessSimulationConsentAccessTests
{
    private const string TeamKey = "team-1";
    private const string UserKey = "user-1";

    private sealed record FakeTeam(string Key, AccessLevel? ConsentAccessLevel) : ITeam
    {
        public string Name => Key;
        public string Icon => null;

        /// <summary>A consented team names the roles it consented to; the grant resolver checks the caller holds one.</summary>
        public string[] ConsentedRoles => [Roles.Developer];
    }

    private sealed class FakeAuthStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private static async IAsyncEnumerable<ITeam> Teams(params ITeam[] teams)
    {
        foreach (var team in teams) yield return team;
        await Task.CompletedTask;
    }

    private static AccessSimulationState Build(bool isMember, bool consented, params string[] scopes)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserKey),
            new Claim(ClaimTypes.Role, Roles.Developer),
            new Claim(Constants.TeamKeyCookie, TeamKey)
        ], "Test"));

        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamMemberAsync(TeamKey, UserKey))
            .ReturnsAsync(isMember ? Mock.Of<ITeamMember>(m => m.Key == "member-1" && m.AccessLevel == AccessLevel.Owner) : null);
        teamService.Setup(x => x.GetConsentedTeamsAsync(It.IsAny<string[]>()))
            .Returns(consented ? Teams(new FakeTeam(TeamKey, AccessLevel.Administrator)) : Teams());

        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));

        var scopeRegistry = new Mock<IScopeRegistry>();
        scopeRegistry.Setup(x => x.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(scopes);

        var options = new ThargaBlazorOptions();
        options.Simulation.Enabled = true;

        // NavigationManager and IJSRuntime are only touched when starting or stopping, which this does not.
        return new AccessSimulationState(
            new FakeAuthStateProvider(principal),
            teamService.Object,
            userService.Object,
            navigationManager: null,
            jsRuntime: null,
            Options.Create(options),
            scopeRegistry.Object);
    }

    /// <summary>The reported bug.</summary>
    [Fact]
    public async Task ACallerWithConsentAccessButNoMembership_CanSimulate()
    {
        var state = Build(isMember: false, consented: true, SimulationScopes.Simulate, "orders:read");

        Assert.True(await state.CanSimulateAsync());
    }

    /// <summary>The self-check: the same setup without consent must refuse, or the test above proves nothing.</summary>
    [Fact]
    public async Task ACallerWithNeitherMembershipNorConsent_CannotSimulate()
    {
        var state = Build(isMember: false, consented: false, SimulationScopes.Simulate);

        Assert.False(await state.CanSimulateAsync());
    }

    [Fact]
    public async Task AMemberHoldingTheScope_CanSimulate()
    {
        var state = Build(isMember: true, consented: false, SimulationScopes.Simulate, "orders:read");

        Assert.True(await state.CanSimulateAsync());
    }

    /// <summary>Holding access is not the same as being allowed to simulate.</summary>
    [Fact]
    public async Task ACallerWithoutTheScope_CannotSimulate()
    {
        var state = Build(isMember: true, consented: true, "orders:read");

        Assert.False(await state.CanSimulateAsync());
    }

    [Fact]
    public async Task WithTheFeatureOff_NobodyCanSimulate()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Constants.TeamKeyCookie, TeamKey)], "Test"));

        var options = new ThargaBlazorOptions();   // Simulation.Enabled defaults to false.

        var state = new AccessSimulationState(
            new FakeAuthStateProvider(principal),
            Mock.Of<ITeamService>(),
            Mock.Of<IUserService>(),
            navigationManager: null,
            jsRuntime: null,
            Options.Create(options));

        Assert.False(await state.CanSimulateAsync());
    }
}
