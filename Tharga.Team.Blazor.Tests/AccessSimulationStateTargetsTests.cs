using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What the simulation dialog is offered to compose from.
/// </summary>
public class AccessSimulationStateTargetsTests
{
    private const string TeamKey = "team-1";
    private const string UserKey = "user-1";

    private sealed class FakeAuthStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private sealed record FakeMember(string Key, AccessLevel AccessLevel, string[] TenantRoles, string[] ScopeOverrides) : ITeamMember
    {
        public string Name => Key;
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public MembershipState? State => MembershipState.Member;
    }

    private static AccessSimulationState Build(string[] callerScopes, ScopeDefinition[] registered, params ITeamMember[] members)
        => Build(callerScopes, registered, roleRegistry: null, members);

    private static AccessSimulationState Build(string[] callerScopes, ScopeDefinition[] registered, ITenantRoleRegistry roleRegistry, params ITeamMember[] members)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserKey), new Claim(Constants.TeamKeyCookie, TeamKey)], "Test"));

        var self = new FakeMember(UserKey, AccessLevel.Administrator, [], []);

        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync(self);
        teamService.Setup(x => x.GetMembersAsync(TeamKey)).Returns(members.ToAsyncEnumerable());

        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));
        userService.Setup(x => x.GetCurrentUserAsync()).ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));

        var scopeRegistry = new Mock<IScopeRegistry>();
        scopeRegistry.SetupGet(x => x.All).Returns(registered);
        scopeRegistry.Setup(x => x.GetEffectiveScopes(AccessLevel.Administrator, It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(callerScopes);
        scopeRegistry.Setup(x => x.GetEffectiveScopes(It.Is<AccessLevel>(l => l != AccessLevel.Administrator), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(["orders:read"]);
        scopeRegistry.Setup(x => x.GetScopesForAccessLevel(It.IsAny<AccessLevel>())).Returns(["orders:read"]);

        var options = new ThargaBlazorOptions();
        options.Simulation.Enabled = true;

        return new AccessSimulationState(
            new FakeAuthStateProvider(principal),
            teamService.Object,
            userService.Object,
            navigationManager: null,
            jsRuntime: null,
            Options.Create(options),
            scopeRegistry.Object,
            tenantRoleRegistry: roleRegistry);
    }

    /// <summary>
    /// Code-registered roles without dynamic roles enabled. The role list used to come only from
    /// <see cref="ITenantRoleService"/>, so a host with roles registered in code could not simulate one.
    /// </summary>
    [Fact]
    public async Task RoleTargets_FallBackToCodeRegisteredRoles_WithoutDynamicRoles()
    {
        var registry = new TenantRoleRegistry();
        registry.Register("Editor", ["orders:write"]);

        var role = Assert.Single(await Build([], [], registry).GetRoleTargetsAsync());

        Assert.Equal("Editor", role.Name);
        Assert.Equal(["orders:write"], role.Scopes);
    }

    [Fact]
    public void AccessLevelTargets_LeaveOutCustom()
    {
        var levels = Build([], []).GetAccessLevelTargets();

        Assert.DoesNotContain(levels, l => l.AccessLevel == AccessLevel.Custom);
        Assert.Equal(Enum.GetValues<AccessLevel>().Length - 1, levels.Count);
    }

    [Fact]
    public async Task MemberTargets_CarryTheMembersRolesAndOverrides()
    {
        var bob = new FakeMember("bob", AccessLevel.Viewer, ["Editor"], ["reports:export"]);

        var target = Assert.Single(await Build([], [], bob).GetMemberTargetsAsync());

        Assert.Equal(["Editor"], target.Roles);
        Assert.Equal(["reports:export"], target.ScopeOverrides);
    }

    [Fact]
    public async Task ScopeChoices_ListEveryRegisteredScope_MarkingWhatTheCallerHolds()
    {
        var state = Build(
            callerScopes: ["orders:read"],
            registered: [new ScopeDefinition("orders:read", AccessLevel.Viewer, "Read orders"), new ScopeDefinition("billing:manage", AccessLevel.Administrator)]);

        var choices = await state.GetScopeChoicesAsync();

        Assert.Equal(["billing:manage", "orders:read"], choices.Select(c => c.Name));
        Assert.False(choices.Single(c => c.Name == "billing:manage").Held);
        Assert.True(choices.Single(c => c.Name == "orders:read").Held);
        Assert.Equal("Read orders", choices.Single(c => c.Name == "orders:read").Description);
    }

    /// <summary>
    /// An override or runtime role can carry a scope nobody registered. The caller holds it, so it is listed —
    /// otherwise a simulation of a member holding it could never show it.
    /// </summary>
    [Fact]
    public async Task ScopeChoices_IncludeAHeldScopeThatIsNotRegistered()
    {
        var choices = await Build(callerScopes: ["legacy:thing"], registered: []).GetScopeChoicesAsync();

        Assert.True(Assert.Single(choices).Held);
    }
}
