using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Which team a request acts on, decided from the credential and a header — never from a parameter.
/// </summary>
/// <remarks>
/// A team API key is bound to one team and can be nothing else, so a team parameter beside it would be a
/// second source of truth for one question. They can disagree, and an API shaped to allow that is wrong
/// even though the disagreement is refused — <c>TeamKeyConfinementTests</c> proves it is. The check is
/// right; the parameter should not have existed to need it.
/// </remarks>
public class TeamContextResolverTests
{
    private const string OwnTeam = "team-1";
    private const string OtherTeam = "team-2";

    private sealed record FakeTeam(string Key, string[] ConsentedRoles, AccessLevel? ConsentAccessLevel) : ITeam
    {
        public string Name => Key;
        public string Icon => null;
    }

    /// <remarks>
    /// Carries <c>ApiKeyId</c>, which is what makes it a <i>team key</i> rather than a user who has
    /// selected a team. Only a key is bound; a person naming another team they belong to is re-selecting,
    /// not contradicting themselves. The first version of these tests omitted it and asserted a user
    /// would be refused — which would have broken team selection for people.
    /// </remarks>
    private static ClaimsPrincipal TeamKeyCaller(string teamKey)
        => new(new ClaimsIdentity(
            [new Claim(TeamClaimTypes.TeamKey, teamKey), new Claim(TeamClaimTypes.ApiKeyId, "key-1")], "Test"));

    private static ClaimsPrincipal SystemKeyCaller()
        => new(new ClaimsIdentity([new Claim(TeamClaimTypes.IsSystemKey, "true")], "Test"));

    private static TeamContextResolver Build(ITeam team = null, params string[] scopesAtLevel)
    {
        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamByKeyAsync(Arg.Any<string>()).Returns((ITeam)null);
        if (team != null) teamService.GetTeamByKeyAsync(team.Key).Returns(team);

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(scopesAtLevel.Length == 0 ? ["team:read"] : scopesAtLevel);
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), PrincipalKind.ApiKey)
            .Returns(scopesAtLevel.Length == 0 ? ["team:read"] : scopesAtLevel);

        return new TeamContextResolver(teamService, registry);
    }

    // ---------------- a team key ----------------

    [Fact]
    public async Task ATeamKey_ActsOnItsOwnTeam_WithNoHeader()
    {
        var context = await Build().ResolveAsync(TeamKeyCaller(OwnTeam), headerTeamKey: null);

        Assert.False(context.IsRefused);
        Assert.Equal(OwnTeam, context.TeamKey);
        Assert.Null(context.Scopes);   // already on its claims; nothing granted here
    }

    /// <summary>A header naming its own team is consistent, not a contradiction.</summary>
    [Fact]
    public async Task ATeamKey_MayNameItsOwnTeam()
    {
        var context = await Build().ResolveAsync(TeamKeyCaller(OwnTeam), OwnTeam);

        Assert.False(context.IsRefused);
        Assert.Equal(OwnTeam, context.TeamKey);
    }

    /// <summary>
    /// <b>Refused, not ignored.</b> The request has said two incompatible things; answering as though the
    /// header were absent would leave the caller believing they asked for something they did not get.
    /// </summary>
    [Fact]
    public async Task ATeamKey_NamingAnotherTeam_IsRefused()
    {
        var context = await Build().ResolveAsync(TeamKeyCaller(OwnTeam), OtherTeam);

        Assert.True(context.IsRefused);
        Assert.Equal(TeamContextRefusal.Contradiction, context.Refusal);
    }

    [Fact]
    public async Task ATeamKey_NamingItsOwnTeamInDifferentCase_IsNotAContradiction()
    {
        var context = await Build().ResolveAsync(TeamKeyCaller(OwnTeam), OwnTeam.ToUpperInvariant());

        Assert.False(context.IsRefused);
    }

    // ---------------- a system key ----------------

    /// <summary>No header, no team. A system caller operates on what its system grants authorize.</summary>
    [Fact]
    public async Task ASystemKey_WithNoHeader_HasNoTeamContext()
    {
        var context = await Build().ResolveAsync(SystemKeyCaller(), headerTeamKey: null);

        Assert.False(context.IsRefused);
        Assert.Null(context.TeamKey);
    }

    /// <summary>
    /// The consented <i>level</i> is the grant — a key holds no roles, so the question is whether the
    /// team consented at all, and at what level.
    /// </summary>
    [Fact]
    public async Task ASystemKey_NamingAConsentingTeam_GetsThatLevelsScopes()
    {
        var team = new FakeTeam(OtherTeam, ["Support"], AccessLevel.User);
        var resolver = Build(team, "team:read", "member:manage");

        var context = await resolver.ResolveAsync(SystemKeyCaller(), OtherTeam);

        Assert.False(context.IsRefused);
        Assert.Equal(OtherTeam, context.TeamKey);
        Assert.Equal(["team:read", "member:manage"], context.Scopes);
    }

    /// <summary>A team that named no roles has consented to nothing, whatever level is recorded.</summary>
    [Fact]
    public async Task ASystemKey_NamingATeamThatHasNotConsented_IsRefused()
    {
        var team = new FakeTeam(OtherTeam, [], AccessLevel.Administrator);
        var context = await Build(team).ResolveAsync(SystemKeyCaller(), OtherTeam);

        Assert.True(context.IsRefused);
        Assert.Equal(TeamContextRefusal.NotConsented, context.Refusal);
    }

    /// <summary>
    /// An unknown team is refused the same way a non-consenting one is: answering differently would tell
    /// a caller which team keys are real.
    /// </summary>
    [Fact]
    public async Task ASystemKey_NamingAnUnknownTeam_IsRefusedIdentically()
    {
        var context = await Build().ResolveAsync(SystemKeyCaller(), "no-such-team");

        Assert.True(context.IsRefused);
        Assert.Equal(TeamContextRefusal.NotConsented, context.Refusal);
    }

    /// <summary>
    /// The level decides what the key gets, so a lower consent yields less. Asserted through the registry
    /// rather than by comparing levels, because which scopes a level carries is the registry's to say and
    /// a host can change it.
    /// </summary>
    [Theory]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Administrator)]
    public async Task TheConsentedLevel_IsWhatIsAskedOfTheRegistry(AccessLevel level)
    {
        var team = new FakeTeam(OtherTeam, ["Support"], level);
        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamByKeyAsync(OtherTeam).Returns(team);

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(["team:read"]);
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), PrincipalKind.ApiKey)
            .Returns(["team:read"]);

        await new TeamContextResolver(teamService, registry).ResolveAsync(SystemKeyCaller(), OtherTeam);

        registry.Received(1).GetEffectiveScopes(level, Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), PrincipalKind.ApiKey);
    }

    // ---------------- a person, who is not bound ----------------

    /// <summary>
    /// A user with a team selected may name a different team they belong to. Only a <i>key</i> is bound —
    /// a person re-selecting is not contradicting themselves.
    /// </summary>
    /// <remarks>
    /// This distinction was found by the resolver tests failing after the two paths were unified: the
    /// fixture had represented a team key as "a principal with a TeamKey claim", which is also what a
    /// signed-in user looks like. Had it stayed that way, selecting another team in the UI would have
    /// started being refused as a contradiction.
    /// </remarks>
    [Fact]
    public async Task AUserWithASelectedTeam_MayNameAnotherTeamTheyBelongTo()
    {
        var member = Substitute.For<ITeamMember>();
        member.Key.Returns("user-1");
        member.AccessLevel.Returns(AccessLevel.Administrator);

        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamMemberAsync(OtherTeam, "user-1").Returns(member);

        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("user-1");
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(["team:read"]);
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), PrincipalKind.ApiKey)
            .Returns(["team:read"]);

        // A selected team, and no ApiKeyId -- a person, not a credential bound to one team.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(TeamClaimTypes.TeamKey, OwnTeam)], "Test"));

        var context = await new TeamContextResolver(teamService, registry, null, userService)
            .ResolveAsync(principal, OtherTeam);

        Assert.False(context.IsRefused);
        Assert.Equal(OtherTeam, context.TeamKey);
    }

    /// <summary>A person naming a team they neither belong to nor are consented into is still refused.</summary>
    [Fact]
    public async Task AUser_NamingATeamTheyCannotReach_IsRefused()
    {
        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamMemberAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((ITeamMember)null);
        teamService.GetConsentedTeamsAsync(Arg.Any<string[]>()).Returns(_ => Empty());

        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("user-1");
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(TeamClaimTypes.TeamKey, OwnTeam)], "Test"));

        var context = await new TeamContextResolver(teamService, Substitute.For<IScopeRegistry>(), null, userService)
            .ResolveAsync(principal, OtherTeam);

        Assert.True(context.IsRefused);
    }

    private static async IAsyncEnumerable<ITeam> Empty()
    {
        yield break;
    }
}
