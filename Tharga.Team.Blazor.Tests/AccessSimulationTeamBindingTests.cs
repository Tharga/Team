using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A simulation applies only to the team it was started in (Tharga/Team#276).
/// </summary>
/// <remarks>
/// <b>The defect these pin.</b> A simulation carried scopes and a level but not its team. Started in a team the
/// caller reached by consent, the reload judged the selection from the narrowed principal, fell back to one of
/// the caller's own teams — and applied the simulation there, a scope set that belonged to neither team.
/// Binding the simulation to its team means another team's request simply does not see it.
/// </remarks>
public class AccessSimulationTeamBindingTests
{
    private const string TeamA = "team-a";
    private const string TeamB = "team-b";
    private const string UserKey = "user-1";

    private static AccessSimulation KeepOnly(string teamKey, params string[] scopes)
        => new() { Kind = AccessSimulationKind.Scopes, Label = "test", Scopes = scopes, TeamKey = teamKey };

    private static string[] ScopesOf(ClaimsPrincipal p)
        => p.FindAll(TeamClaimTypes.Scope).Select(c => c.Value).ToArray();

    // --- the cookie read ---

    [Fact]
    public void ReadForTeam_ReturnsTheSimulation_ForItsOwnTeam()
        => Assert.NotNull(AccessSimulationCookie.ReadForTeam(AccessSimulationCookie.Write(KeepOnly(TeamA, "x")), TeamA));

    [Fact]
    public void ReadForTeam_IgnoresTheSimulation_OnAnotherTeam()
        => Assert.Null(AccessSimulationCookie.ReadForTeam(AccessSimulationCookie.Write(KeepOnly(TeamA, "x")), TeamB));

    /// <summary>
    /// A cookie written before simulations carried a team. It cannot say where it belongs, so it applies nowhere
    /// and the caller sees their real access — the direction a cookie that does not apply always takes.
    /// </summary>
    [Fact]
    public void ReadForTeam_IgnoresASimulationWithNoTeam()
        => Assert.Null(AccessSimulationCookie.ReadForTeam(AccessSimulationCookie.Write(KeepOnly(null, "x")), TeamA));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReadForTeam_IgnoresTheSimulation_WhenNoTeamIsSelected(string selected)
        => Assert.Null(AccessSimulationCookie.ReadForTeam(AccessSimulationCookie.Write(KeepOnly(TeamA, "x")), selected));

    [Fact]
    public void ReadForTeam_ComparesTeamKeysExactly()
        => Assert.Null(AccessSimulationCookie.ReadForTeam(AccessSimulationCookie.Write(KeepOnly("Team-A", "x")), "team-a"));

    [Fact]
    public void TheTeamKeySurvivesARoundTrip()
        => Assert.Equal(TeamA, AccessSimulationCookie.Read(AccessSimulationCookie.Write(KeepOnly(TeamA, "x"))).TeamKey);

    // --- path 1: the HTTP transformation ---

    [Fact]
    public async Task TheHttpTransformation_AppliesTheSimulationOnItsOwnTeam()
    {
        var result = await TransformAsync(selectedTeam: TeamA, KeepOnly(TeamA, "orders:read"));

        Assert.Equal(["orders:read"], ScopesOf(result));
        Assert.True(AccessSimulationCookie.IsActive(result));
    }

    /// <summary>
    /// #276 step 5 of the cause: the same cookie reaching a request for Team B. It must neither narrow Team B nor
    /// mark the session as simulating — otherwise the banner describes a simulation that is not in force.
    /// </summary>
    [Fact]
    public async Task TheHttpTransformation_IgnoresTheSimulationOnAnotherTeam()
    {
        var result = await TransformAsync(selectedTeam: TeamB, KeepOnly(TeamA, "orders:read"));

        Assert.Equal(["orders:read", "orders:write", "billing:manage"], ScopesOf(result));
        Assert.False(AccessSimulationCookie.IsActive(result));
    }

    // --- path 2: the in-circuit revalidator ---

    [Fact]
    public async Task TheRevalidator_KeepsTheSimulationOnItsOwnTeam()
    {
        var refreshed = await RevalidateAsync(selectedTeam: TeamA, KeepOnly(TeamA, "orders:read"));

        Assert.Equal(["orders:read"], ScopesOf(refreshed));
    }

    [Fact]
    public async Task TheRevalidator_DoesNotApplyASimulationForAnotherTeam()
    {
        var refreshed = await RevalidateAsync(selectedTeam: TeamB, KeepOnly(TeamA, "orders:read"));

        Assert.Equal(["orders:read", "orders:write", "billing:manage"], ScopesOf(refreshed));
    }

    // --- starting one ---

    [Fact]
    public async Task Starting_BindsTheSimulationToTheSelectedTeam()
    {
        var written = await StartAsync(selectedTeam: TeamA, KeepOnly(teamKey: null, "orders:read"));

        Assert.Equal(TeamA, AccessSimulationCookie.Read(written).TeamKey);
    }

    /// <summary>
    /// A caller-supplied team key is overwritten, not trusted. The team is the one selected when it started.
    /// </summary>
    [Fact]
    public async Task Starting_OverridesAnyTeamKeyTheSimulationAlreadyCarried()
    {
        var written = await StartAsync(selectedTeam: TeamA, KeepOnly(TeamB, "orders:read"));

        Assert.Equal(TeamA, AccessSimulationCookie.Read(written).TeamKey);
    }

    // --- fakes ---

    private sealed record FakeMember(string Key, AccessLevel AccessLevel) : ITeamMember
    {
        public string Name => "Alice";
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public MembershipState? State => MembershipState.Member;
        public string[] TenantRoles => [];
        public string[] ScopeOverrides => [];
    }

    private static TeamMembershipClaimsBuilder Builder()
    {
        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamMemberAsync(It.IsAny<string>(), UserKey))
            .ReturnsAsync(new FakeMember("member-1", AccessLevel.Owner));

        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));

        var scopeRegistry = new Mock<IScopeRegistry>();
        scopeRegistry.Setup(x => x.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(["orders:read", "orders:write", "billing:manage"]);

        return new TeamMembershipClaimsBuilder(teamService.Object, userService.Object, Options.Create(new ThargaBlazorOptions()), scopeRegistry.Object);
    }

    private static async Task<ClaimsPrincipal> TransformAsync(string selectedTeam, AccessSimulation simulation)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie =
            $"{Constants.SelectedTeamKeyCookie}={selectedTeam}; " +
            $"{AccessSimulationCookie.Name}={AccessSimulationCookie.Write(simulation)}";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(x => x.HttpContext).Returns(httpContext);

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "Test"));
        return await new TeamServerClaimsTransformation(accessor.Object, Builder()).TransformAsync(principal);
    }

    private static async Task<ClaimsPrincipal> RevalidateAsync(string selectedTeam, AccessSimulation simulation)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "alice"),
            new Claim(Constants.TeamKeyCookie, selectedTeam),
            new Claim(TeamClaimTypes.TeamKey, selectedTeam),
            new Claim(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Owner)),
            new Claim(TeamClaimTypes.Scope, "orders:read"),
            new Claim(AccessSimulationCookie.ClaimType, AccessSimulationCookie.Write(simulation))
        ], "Test"));

        return await new TeamClaimRevalidator(Builder()).TryRefreshAsync(principal) ?? principal;
    }

    private sealed class FakeAuthStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager() => Initialize("https://localhost/", "https://localhost/page");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private static async Task<string> StartAsync(string selectedTeam, AccessSimulation simulation)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserKey), new Claim(Constants.TeamKeyCookie, selectedTeam)], "Test"));

        string script = null;
        var js = new Mock<IJSRuntime>();
        js.Setup(x => x.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>("eval", It.IsAny<object[]>()))
            .Callback<string, object[]>((_, args) => script = (string)args[0])
            .Returns(ValueTask.FromResult<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(null));

        var options = new ThargaBlazorOptions();
        options.Simulation.Enabled = true;

        var state = new AccessSimulationState(
            new FakeAuthStateProvider(principal),
            Mock.Of<ITeamService>(),
            Mock.Of<IUserService>(),
            new FakeNavigationManager(),
            js.Object,
            Options.Create(options));

        await state.StartAsync(simulation);

        Assert.NotNull(script);
        var prefix = $"document.cookie = '{AccessSimulationCookie.Name}=";
        var start = script.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        return script[start..script.IndexOf(';', start)];
    }
}
