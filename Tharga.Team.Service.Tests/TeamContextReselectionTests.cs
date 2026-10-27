using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A signed-in person naming a team in the header re-selects: the team claims they carried are replaced by the
/// named team's, never kept beside them.
/// </summary>
/// <remarks>
/// The fixture is a person who is a Viewer on the team they selected (A) and an Administrator of another (B).
/// After naming B, the principal describes B only — so B's scopes act on B, and nothing acts on A with them.
/// </remarks>
public class TeamContextReselectionTests
{
    private const string Header = "X-Team-Key";
    private const string TeamA = "team-a";
    private const string TeamB = "team-b";
    private const string UserKey = "user-1";

    private static ClaimsPrincipal ViewerOnTeamA()
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "subject-1"),
            new Claim(ClaimTypes.Role, "Developer"),
            new Claim(TeamClaimTypes.SystemScope, "audit:read"),
            new Claim(TeamClaimTypes.TeamKey, TeamA),
            new Claim(ClaimTypes.Role, "TeamMember"),
            new Claim(ClaimTypes.Role, "TeamViewer"),
            new Claim(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Viewer)),
            new Claim(TeamClaimTypes.MemberKey, "member-a"),
            new Claim(TeamClaimTypes.Scope, TeamScopes.Read),
        ], "Cookies"));

    private static async Task<(HttpContext Context, bool Continued)> RunAsync(ClaimsPrincipal principal, string headerValue)
    {
        var memberOfA = Substitute.For<ITeamMember>();
        memberOfA.Key.Returns("member-a");
        memberOfA.AccessLevel.Returns(AccessLevel.Viewer);

        var memberOfB = Substitute.For<ITeamMember>();
        memberOfB.Key.Returns("member-b");
        memberOfB.AccessLevel.Returns(AccessLevel.Administrator);

        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamMemberAsync(TeamA, UserKey).Returns(memberOfA);
        teamService.GetTeamMemberAsync(TeamB, UserKey).Returns(memberOfB);

        var user = Substitute.For<IUser>();
        user.Key.Returns(UserKey);
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(AccessLevel.Viewer, Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns([TeamScopes.Read]);
        registry.GetEffectiveScopes(AccessLevel.Administrator, Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns([TeamScopes.Read, TeamScopes.MemberManage]);

        var context = new DefaultHttpContext { User = principal };
        context.Response.Body = new MemoryStream();
        if (headerValue != null) context.Request.Headers[Header] = headerValue;

        var continued = false;
        var sut = new TeamContextMiddleware(_ => { continued = true; return Task.CompletedTask; },
            Options.Create(new TeamContextOptions()));

        await sut.InvokeAsync(context, new TeamContextResolver(teamService, registry, null, userService));

        return (context, continued);
    }

    [Fact]
    public async Task NamingAnotherTeam_DoesNotGrantItsScopesOnTheSelectedTeam()
    {
        var (context, continued) = await RunAsync(ViewerOnTeamA(), TeamB);

        Assert.True(continued);
        Assert.False(TeamScopePolicy.HasTeamScope(context.User, TeamScopes.MemberManage, TeamA));
        Assert.False(TeamScopePolicy.HasTeamScope(context.User, TeamScopes.Read, TeamA));
    }

    [Fact]
    public async Task NamingAnotherTeam_ActsOnThatTeamWithItsScopes()
    {
        var (context, _) = await RunAsync(ViewerOnTeamA(), TeamB);

        Assert.True(TeamScopePolicy.HasTeamScope(context.User, TeamScopes.MemberManage, TeamB));
        Assert.True(TeamScopePolicy.HasDirectTeamScope(context.User, TeamScopes.MemberManage, TeamB));
    }

    [Fact]
    public async Task NamingAnotherTeam_ReplacesTheSelectedTeamsClaims()
    {
        var (context, _) = await RunAsync(ViewerOnTeamA(), TeamB);
        var user = context.User;

        Assert.Equal([TeamB], user.FindAll(TeamClaimTypes.TeamKey).Select(c => c.Value));
        Assert.Equal(["member-b"], user.FindAll(TeamClaimTypes.MemberKey).Select(c => c.Value));
        Assert.DoesNotContain(user.Claims, c => c.Type == TeamClaimTypes.AccessLevel);
        Assert.False(user.IsInRole("TeamViewer"));
        Assert.False(user.IsInRole("TeamMember"));
    }

    /// <summary>What does not describe a team stays: the subject, app roles and system grants.</summary>
    [Fact]
    public async Task NamingAnotherTeam_KeepsClaimsThatAreNotAboutATeam()
    {
        var (context, _) = await RunAsync(ViewerOnTeamA(), TeamB);
        var user = context.User;

        Assert.True(user.Identity?.IsAuthenticated);
        Assert.Equal("Cookies", user.Identity?.AuthenticationType);
        Assert.Equal("subject-1", user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.True(user.IsInRole("Developer"));
        Assert.True(TeamScopePolicy.HasSystemScope(user, "audit:read"));
    }

    /// <summary>The principal it started from is not changed under anything else that holds it.</summary>
    [Fact]
    public async Task NamingAnotherTeam_LeavesTheOriginalPrincipalAsItWas()
    {
        var original = ViewerOnTeamA();

        await RunAsync(original, TeamB);

        Assert.Single(original.Identities);
        Assert.Equal(TeamA, original.FindFirst(TeamClaimTypes.TeamKey)?.Value);
        Assert.Equal(nameof(AccessLevel.Viewer), original.FindFirst(TeamClaimTypes.AccessLevel)?.Value);
    }

    /// <summary>Naming the team the caller is already on changes nothing; its own claims for that team stand.</summary>
    [Fact]
    public async Task NamingTheSelectedTeam_LeavesTheClaimsAsTheyAre()
    {
        var principal = ViewerOnTeamA();

        var (context, continued) = await RunAsync(principal, TeamA);

        Assert.True(continued);
        Assert.Single(context.User.Identities);
        Assert.Equal(nameof(AccessLevel.Viewer), context.User.FindFirst(TeamClaimTypes.AccessLevel)?.Value);
        Assert.True(TeamScopePolicy.HasTeamScope(context.User, TeamScopes.Read, TeamA));
        Assert.False(TeamScopePolicy.HasTeamScope(context.User, TeamScopes.MemberManage, TeamA));
    }
}
