using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Changing a team's consent requires <c>team:manage</c> held as a member or a team key — never through the consent
/// itself.
/// </summary>
/// <remarks>
/// <b>The hole this closes.</b> A caller consented into a team at Administrator holds <c>team:manage</c> there, exactly
/// as a member does. Once consent can be time-bound, letting that caller change consent lets the grant extend itself:
/// remove its own expiry, or raise its own level. The claims carry the provenance — a member key comes only from a
/// membership — so the check reads it.
/// </remarks>
public class DirectTeamScopeTests
{
    private const string Team = "T1";

    private static ClaimsPrincipal Caller(string teamKey, bool memberKey = false, bool apiKey = false, bool systemKey = false)
    {
        var claims = new List<Claim> { new(TeamClaimTypes.TeamKey, teamKey), new(TeamClaimTypes.Scope, TeamScopes.Manage) };
        if (memberKey) claims.Add(new Claim(TeamClaimTypes.MemberKey, "member-1"));
        if (apiKey) claims.Add(new Claim(TeamClaimTypes.ApiKeyId, "key-1"));
        if (systemKey) claims.Add(new Claim(TeamClaimTypes.IsSystemKey, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    // --- the policy ---

    [Fact]
    public void AMember_HoldsItDirectly()
        => Assert.True(TeamScopePolicy.HasDirectTeamScope(Caller(Team, memberKey: true), TeamScopes.Manage, Team));

    [Fact]
    public void ATeamKey_HoldsItDirectly()
        => Assert.True(TeamScopePolicy.HasDirectTeamScope(Caller(Team, apiKey: true), TeamScopes.Manage, Team));

    /// <summary>Scopes and a team key claim, but no membership and no key of the team's own: consent.</summary>
    [Fact]
    public void AConsentedCaller_DoesNot()
        => Assert.False(TeamScopePolicy.HasDirectTeamScope(Caller(Team), TeamScopes.Manage, Team));

    /// <summary>A system key acting on a team through the team's consent is not the team's own key.</summary>
    [Fact]
    public void ASystemKeyThroughConsent_DoesNot()
        => Assert.False(TeamScopePolicy.HasDirectTeamScope(Caller(Team, apiKey: true, systemKey: true), TeamScopes.Manage, Team));

    [Fact]
    public void AMemberOfAnotherTeam_DoesNot()
        => Assert.False(TeamScopePolicy.HasDirectTeamScope(Caller("T2", memberKey: true), TeamScopes.Manage, Team));

    [Fact]
    public void AMemberWithoutTheScope_DoesNot()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(TeamClaimTypes.TeamKey, Team), new Claim(TeamClaimTypes.MemberKey, "member-1")], "Test"));

        Assert.False(TeamScopePolicy.HasDirectTeamScope(principal, TeamScopes.Manage, Team));
    }

    // --- consent changes ---

    private static (AuthorizationTeamServiceDecorator Sut, ITeamService Inner) Build(ClaimsPrincipal principal)
    {
        var inner = Substitute.For<ITeamService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        return (new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions()), inner);
    }

    [Fact]
    public async Task SetConsent_ByAMember_Delegates()
    {
        var (sut, inner) = Build(Caller(Team, memberKey: true));

        await sut.SetTeamConsentAsync(Team, ["Developer"], AccessLevel.Viewer);

        await inner.Received(1).SetTeamConsentAsync(Team, Arg.Any<string[]>(), AccessLevel.Viewer);
    }

    [Fact]
    public async Task SetConsent_ByATeamKey_Delegates()
    {
        var (sut, inner) = Build(Caller(Team, apiKey: true));

        await sut.SetTeamConsentAsync(Team, ["Developer"], AccessLevel.Viewer);

        await inner.Received(1).SetTeamConsentAsync(Team, Arg.Any<string[]>(), AccessLevel.Viewer);
    }

    /// <summary>The escalation this prevents: a consented Administrator removing its own consent window.</summary>
    [Fact]
    public async Task SetConsent_ByACallerConsentedIn_IsRefusedWithTheReason()
    {
        var (sut, inner) = Build(Caller(Team));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetTeamConsentAsync(Team, ["Developer"], AccessLevel.Administrator));

        Assert.Contains("through its consent", ex.Message);
        await inner.DidNotReceiveWithAnyArgs().SetTeamConsentAsync(default, default, default);
    }

    // --- the member key reaches REST callers ---

    [Fact]
    public async Task ATeamContextForAMember_CarriesTheirMemberKey()
    {
        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamMemberAsync(Team, "user-1").Returns(new Member());

        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("user-1");
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>()).Returns(["team:manage"]);

        var person = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Test"));
        var context = await new TeamContextResolver(teamService, registry, userService: userService).ResolveAsync(person, Team);

        Assert.Equal("member-1", context.MemberKey);
    }

    private sealed record Member : ITeamMember
    {
        public string Key => "member-1";
        public string Name => "Alice";
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public AccessLevel AccessLevel => AccessLevel.Administrator;
        public MembershipState? State => MembershipState.Member;
        public string[] TenantRoles => [];
        public string[] ScopeOverrides => [];
    }
}
