using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Cross-team discovery (<c>ITeamService.GetAllTeamsAsync</c>) gated on the
/// <see cref="SystemTeamScopes.Read"/> system scope, plus the loud default on
/// <see cref="TeamServiceBase"/> for services that don't support it.
/// </summary>
public class CrossTeamListingTests
{
    private static (AuthorizationTeamServiceDecorator sut, ITeamService inner) Build(ClaimsPrincipal principal)
    {
        var inner = Substitute.For<ITeamService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        var sut = new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions { AllowTeamCreation = true });
        return (sut, inner);
    }

    private static ClaimsPrincipal Principal(string teamKey, params string[] scopes)
        => Principal(teamKey, scopes, []);

    /// <summary>
    /// Team scopes and system scopes are separate claim types, so a fixture has to say which it is granting.
    /// A team-level grant must not satisfy a system check, and vice versa.
    /// </summary>
    private static ClaimsPrincipal Principal(string teamKey, string[] scopes, string[] systemScopes)
    {
        var claims = new List<Claim>();
        if (teamKey != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, teamKey));
        foreach (var s in scopes) claims.Add(new Claim(TeamClaimTypes.Scope, s));
        foreach (var s in systemScopes) claims.Add(new Claim(TeamClaimTypes.SystemScope, s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async IAsyncEnumerable<T> Stream<T>(params T[] items)
    {
        await Task.CompletedTask;
        foreach (var item in items) yield return item;
    }

    private static async Task<int> CountAsync(IAsyncEnumerable<ITeam> source)
    {
        var count = 0;
        await foreach (var _ in source) count++;
        return count;
    }

    [Fact]
    public async Task GetAllTeams_WithTeamsRead_Delegates()
    {
        var (sut, inner) = Build(Principal("T1", [], [SystemTeamScopes.Read]));
        inner.GetAllTeamsAsync().Returns(Stream(Substitute.For<ITeam>(), Substitute.For<ITeam>()));

        Assert.Equal(2, await CountAsync(sut.GetAllTeamsAsync()));
    }

    [Fact]
    public async Task GetAllTeams_WithoutTeamsRead_Throws()
    {
        var (sut, inner) = Build(Principal("T1"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await CountAsync(sut.GetAllTeamsAsync()));
        inner.DidNotReceive().GetAllTeamsAsync();
    }

    /// <summary>
    /// The in-team manage scope must not stand in for the system scope — that conflation is the whole
    /// bug class this feature has to avoid.
    /// </summary>
    [Fact]
    public async Task GetAllTeams_TeamManageIsNotEnough_Throws()
    {
        var (sut, _) = Build(Principal("T1", TeamScopes.Manage));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await CountAsync(sut.GetAllTeamsAsync()));
    }

    [Fact]
    public async Task GetAllTeams_TeamsDeleteIsNotEnough_Throws()
    {
        var (sut, _) = Build(Principal("T1", SystemTeamScopes.Delete));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await CountAsync(sut.GetAllTeamsAsync()));
    }

    [Fact]
    public async Task GetAllTeams_Unauthenticated_Throws()
    {
        var (sut, _) = Build(new ClaimsPrincipal(new ClaimsIdentity()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await CountAsync(sut.GetAllTeamsAsync()));
    }

    /// <summary>
    /// A service deriving from <see cref="TeamServiceBase"/> without cross-team support still compiles (the
    /// member is virtual, not abstract), but says so when asked instead of yielding nothing. An empty list
    /// reads as "this caller has no teams": granting <see cref="SystemTeamScopes.Read"/> to such a host
    /// emptied the team page for every user, Owners included, with nothing in the log.
    /// </summary>
    [Fact]
    public async Task TeamServiceBase_DefaultGetAllTeams_Throws()
    {
        var sut = new TestTeamService(Substitute.For<IUserService>());

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () => await CountAsync(sut.GetAllTeamsAsync()));
        Assert.Contains(nameof(TestTeamService), ex.Message);
        Assert.Contains("GetAllTeamsInternalAsync", ex.Message);
        Assert.Contains(SystemTeamScopes.Read, ex.Message);
    }

    [Fact]
    public async Task TeamServiceBase_DefaultGetAllTeamsOfMember_Throws()
    {
        var sut = new TestTeamService(Substitute.For<IUserService>());

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in sut.GetAllTeamsAsync<TestMember>()) { }
        });
    }

    [Fact]
    public async Task TeamServiceBase_OverriddenGetAllTeams_IsHonoured()
    {
        var sut = new EnumeratingTeamService(Substitute.For<IUserService>());
        sut.AddTeam("T1", "One");
        sut.AddTeam("T2", "Two");

        Assert.Equal(2, await CountAsync(sut.GetAllTeamsAsync()));
    }

    /// <summary>
    /// The throw is reachable only by a <see cref="SystemTeamScopes.Read"/> holder: everyone else is refused
    /// by the decorator before the store is asked, so a host that never grants the scope is unaffected.
    /// </summary>
    [Fact]
    public async Task TeamServiceBase_DefaultGetAllTeams_WithoutTeamsRead_IsRefusedBeforeTheStore()
    {
        var inner = new TestTeamService(Substitute.For<IUserService>());
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(Principal("T1", TeamScopes.Manage)));
        var sut = new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await CountAsync(sut.GetAllTeamsAsync()));
    }

    private sealed class EnumeratingTeamService(IUserService userService) : TestTeamService(userService)
    {
        protected override async IAsyncEnumerable<ITeam> GetAllTeamsInternalAsync()
        {
            foreach (var key in new[] { "T1", "T2" })
            {
                yield return await GetTeamAsync(key);
            }
        }
    }
}
