using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Demo mode is a separate grant from run-as, and a <b>system</b> one. Tharga/Team#223.
/// </summary>
/// <remarks>
/// <b>The defect these pin.</b> Both halves used to sit behind <c>simulation:use</c>, registered at
/// <c>AccessLevel.Administrator</c> — and Owner and Administrator receive every registered scope, so the
/// grant reached every team owner and administrator in every tenant, with no way for a host to narrow it.
/// Demo mode drops system scopes and application roles, so for a customer's own team owner it offered to
/// drop nothing: inert for exactly the audience that saw it.
/// <para>
/// The split is what fixes it, so the load-bearing assertions are the two <i>refusals</i> — a run-as holder
/// cannot demo, and an in-team claim spelled <c>simulation:demo</c> does not satisfy a system check. Get the
/// second wrong and any team administrator who can name a scope regains the capability.
/// </para>
/// </remarks>
public class AccessSimulationDemoGateTests
{
    private const string TeamKey = "team-1";
    private const string UserKey = "user-1";

    private sealed class FakeAuthStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private static ClaimsPrincipal Principal(string[] teamScopes = null, string[] systemScopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, UserKey),
            new(Constants.TeamKeyCookie, TeamKey),
            new(TeamClaimTypes.TeamKey, TeamKey)
        };

        foreach (var scope in teamScopes ?? []) claims.Add(new Claim(TeamClaimTypes.Scope, scope));
        foreach (var scope in systemScopes ?? []) claims.Add(new Claim(TeamClaimTypes.SystemScope, scope));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static AccessSimulationState Build(ClaimsPrincipal principal, bool enabled = true)
    {
        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamMemberAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Mock.Of<ITeamMember>(m => m.Key == "member-1" && m.AccessLevel == AccessLevel.Administrator));

        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));

        var scopeRegistry = new Mock<IScopeRegistry>();
        scopeRegistry.Setup(x => x.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns<string[]>(null);

        var options = new ThargaBlazorOptions();
        options.Simulation.Enabled = enabled;

        return new AccessSimulationState(
            new FakeAuthStateProvider(principal),
            teamService.Object,
            userService.Object,
            navigationManager: null,
            jsRuntime: null,
            Options.Create(options),
            scopeRegistry.Object);
    }

    [Fact]
    public async Task WithTheSystemGrant_DemoIsAllowed()
    {
        var state = Build(Principal(systemScopes: [SimulationScopes.Demo]));

        Assert.True(await state.CanUseDemoAsync());
    }

    /// <summary>
    /// The case the issue reported: a tenant administrator holds the run-as scope and must not thereby hold
    /// demo mode.
    /// </summary>
    [Fact]
    public async Task WithOnlyTheRunAsScope_DemoIsRefused()
    {
        var state = Build(Principal(teamScopes: [SimulationScopes.Simulate]));

        Assert.False(await state.CanUseDemoAsync());
    }

    /// <summary>
    /// The escalation this guards against. Team scopes and system scopes are separate claim types, and a
    /// team administrator can hold team-level scopes.
    /// </summary>
    [Fact]
    public async Task WithAnInTeamClaimOfTheSameName_DemoIsRefused()
    {
        var state = Build(Principal(teamScopes: [SimulationScopes.Demo]));

        Assert.False(await state.CanUseDemoAsync());
    }

    [Fact]
    public async Task WithNoGrantAtAll_DemoIsRefused()
    {
        Assert.False(await Build(Principal()).CanUseDemoAsync());
    }

    [Fact]
    public async Task WhenSimulationIsDisabled_DemoIsRefused()
    {
        var state = Build(Principal(systemScopes: [SimulationScopes.Demo]), enabled: false);

        Assert.False(await state.CanUseDemoAsync());
    }

    /// <summary>
    /// Hiding the control is presentation; refusing the call is the rule. A caller reaching
    /// <c>StartDemoAsync</c> without the grant — by any route — is refused rather than quietly simulating.
    /// </summary>
    [Fact]
    public async Task StartingDemoWithoutTheGrant_Throws()
    {
        var state = Build(Principal(teamScopes: [SimulationScopes.Simulate]));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.StartDemoAsync());

        Assert.Contains(SimulationScopes.Demo, ex.Message);
    }

    /// <summary>
    /// Run-as and demo are different grants, so the run-as answer must not move when the demo scope is the
    /// only thing the caller holds.
    /// </summary>
    [Fact]
    public async Task TheDemoGrantAloneDoesNotAuthorizeRunAs()
    {
        var state = Build(Principal(systemScopes: [SimulationScopes.Demo]));

        Assert.False(await state.CanSimulateAsync());
    }

    [Fact]
    public async Task TheRunAsGrantStillAuthorizesRunAs()
    {
        var state = Build(Principal(teamScopes: [SimulationScopes.Simulate]));

        Assert.True(await state.CanSimulateAsync());
    }

    /// <summary>
    /// The demo target must be its own kind, not a scope-set simulation wearing a label — the navigation bar
    /// decides what to draw from <see cref="AccessSimulation.Kind"/>, and matching on the label would break
    /// the moment a host translates it.
    /// </summary>
    [Fact]
    public void TheDemoTargetIsItsOwnKind()
    {
        var demo = AccessSimulationTargets.FromDemo(["orders:read"]);

        Assert.Equal(AccessSimulationKind.Demo, demo.Kind);
    }
}
