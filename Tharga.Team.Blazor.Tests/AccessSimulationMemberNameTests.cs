using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// How the simulation picker names the members it offers.
/// </summary>
/// <remarks>
/// <b>Regression cover for the picker being unusable without <c>users:manage</c>.</b> Name resolution used
/// to call <see cref="IUserService.GetUserByKeyAsync"/>, which carries
/// <c>[RequireScope(SystemUserScopes.Manage)]</c> — a <i>system</i> scope no team access level grants. The
/// feature's own scope, <see cref="SimulationScopes.Simulate"/>, is a <i>team</i> scope registered at
/// <c>Administrator</c>, so every caller it was designed for was refused by the user store.
/// <para>
/// It went unnoticed because the fake member in <see cref="AccessSimulationStateTargetsTests"/> always
/// carried a per-team name override, which short-circuits the lookup. In production that override is
/// usually null. Every member here therefore declares <see cref="ITeamMember.Name"/> as null unless the
/// test is specifically about the override.
/// </para>
/// </remarks>
public class AccessSimulationMemberNameTests
{
    private const string TeamKey = "team-1";
    private const string CallerKey = "user-1";

    private sealed class FakeAuthStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private sealed record FakeMember(string Key) : ITeamMember
    {
        public string Name { get; init; }
        public AccessLevel AccessLevel => AccessLevel.Viewer;
        public string[] TenantRoles => [];
        public string[] ScopeOverrides => [];
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public MembershipState? State => MembershipState.Member;
    }

    private sealed record FakeUser(string Key, string Name = null, string EMail = null) : IUser
    {
        public string Identity => Key;
    }

    private static (AccessSimulationState State, Mock<IUserService> UserService) Build(
        bool hasUsersManage,
        ITeamMember[] members,
        IUser[] teamMemberUsers = null,
        IUser[] directoryUsers = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, CallerKey),
            new(Constants.TeamKeyCookie, TeamKey)
        };

        if (hasUsersManage)
            claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemUserScopes.Manage));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var teamService = new Mock<ITeamService>();
        teamService.Setup(x => x.GetMembersAsync(TeamKey)).Returns(members.ToAsyncEnumerable());

        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(new FakeUser(CallerKey));
        userService.Setup(x => x.GetCurrentUserAsync()).ReturnsAsync(new FakeUser(CallerKey));
        userService.Setup(x => x.GetTeamMemberUsersAsync()).ReturnsAsync(teamMemberUsers ?? []);
        userService.Setup(x => x.GetAsync()).Returns((directoryUsers ?? []).ToAsyncEnumerable());

        var scopeRegistry = new Mock<IScopeRegistry>();
        scopeRegistry.SetupGet(x => x.All).Returns([]);
        scopeRegistry.Setup(x => x.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns([]);

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

        return (state, userService);
    }

    /// <summary>
    /// The defect itself: a team Owner or Administrator holds <c>simulation:use</c> but never
    /// <c>users:manage</c>, so the picker has to name members from the co-member projection.
    /// </summary>
    [Fact]
    public async Task MemberNames_ComeFromTheCoMemberProjection_WhenTheCallerLacksUsersManage()
    {
        var (state, userService) = Build(
            hasUsersManage: false,
            members: [new FakeMember("bob")],
            teamMemberUsers: [new FakeUser("bob", "Bob Builder")]);

        var target = Assert.Single(await state.GetMemberTargetsAsync());

        Assert.Equal("Bob Builder", target.Name);
        userService.Verify(x => x.GetTeamMemberUsersAsync(), Times.Once);
    }

    /// <summary>
    /// The gated members stay gated and stay uncalled. Reaching for one is what made the picker
    /// unusable; this asserts the surface no longer does.
    /// </summary>
    [Fact]
    public async Task MemberNames_NeverReachAUsersManageGatedRead_WhenTheCallerLacksUsersManage()
    {
        var (state, userService) = Build(
            hasUsersManage: false,
            members: [new FakeMember("bob")],
            teamMemberUsers: [new FakeUser("bob", "Bob Builder")]);

        await state.GetMemberTargetsAsync();

        userService.Verify(x => x.GetUserByKeyAsync(It.IsAny<string>()), Times.Never);
        userService.Verify(x => x.GetAsync(), Times.Never);
    }

    /// <summary>
    /// A <c>users:manage</c> holder keeps the full directory. They may be acting in a team they reach by
    /// consent rather than membership, and the co-member projection is built from memberships — so
    /// narrowing everyone to it would leave exactly that caller looking at raw keys.
    /// </summary>
    [Fact]
    public async Task MemberNames_ComeFromTheFullDirectory_WhenTheCallerHoldsUsersManage()
    {
        var (state, userService) = Build(
            hasUsersManage: true,
            members: [new FakeMember("bob")],
            directoryUsers: [new FakeUser("bob", "Bob Builder")]);

        var target = Assert.Single(await state.GetMemberTargetsAsync());

        Assert.Equal("Bob Builder", target.Name);
        userService.Verify(x => x.GetAsync(), Times.Once);
        userService.Verify(x => x.GetTeamMemberUsersAsync(), Times.Never);
    }

    /// <summary>The per-team override is a deliberate rename and still wins over the user record.</summary>
    [Fact]
    public async Task MemberName_PrefersThePerTeamOverride()
    {
        var (state, _) = Build(
            hasUsersManage: false,
            members: [new FakeMember("bob") { Name = "Bob (contractor)" }],
            teamMemberUsers: [new FakeUser("bob", "Bob Builder")]);

        var target = Assert.Single(await state.GetMemberTargetsAsync());

        Assert.Equal("Bob (contractor)", target.Name);
    }

    /// <summary>
    /// With no name on the user record the email-derived fallback applies, exactly as the team member
    /// list resolves it.
    /// </summary>
    [Fact]
    public async Task MemberName_FallsBackToTheEmailDerivedName()
    {
        var (state, _) = Build(
            hasUsersManage: false,
            members: [new FakeMember("bob")],
            teamMemberUsers: [new FakeUser("bob", EMail: "bob.builder@example.com")]);

        var target = Assert.Single(await state.GetMemberTargetsAsync());

        Assert.Equal("Bob Builder", target.Name);
    }

    /// <summary>
    /// A member the projection does not carry shows the raw key rather than throwing or rendering
    /// "Unknown" for everyone.
    /// </summary>
    [Fact]
    public async Task MemberName_FallsBackToTheMemberKey_WhenNoUserRecordMatches()
    {
        var (state, _) = Build(
            hasUsersManage: false,
            members: [new FakeMember("bob")],
            teamMemberUsers: []);

        var target = Assert.Single(await state.GetMemberTargetsAsync());

        Assert.Equal("bob", target.Name);
    }

    /// <summary>
    /// One read per dialog open, not one per member. The previous shape was an N+1 against the user
    /// store — a team of fifty cost fifty reads to draw one list.
    /// </summary>
    [Fact]
    public async Task MemberNames_ReadTheUserStoreOnce_NotOncePerMember()
    {
        var (state, userService) = Build(
            hasUsersManage: false,
            members: [new FakeMember("bob"), new FakeMember("carol"), new FakeMember("dave")],
            teamMemberUsers:
            [
                new FakeUser("bob", "Bob Builder"),
                new FakeUser("carol", "Carol Carpenter"),
                new FakeUser("dave", "Dave Digger")
            ]);

        var targets = await state.GetMemberTargetsAsync();

        Assert.Equal(["Bob Builder", "Carol Carpenter", "Dave Digger"], targets.Select(t => t.Name));
        userService.Verify(x => x.GetTeamMemberUsersAsync(), Times.Once);
    }
}
