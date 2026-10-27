using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A team scope counts only on the identity that was issued for that team.
/// </summary>
/// <remarks>
/// A principal can carry more than one identity. The team is read from the first <c>TeamKey</c> claim, so the
/// scope has to come from an identity carrying that same team key — a scope claim elsewhere on the principal was
/// issued for whatever team that identity names, not for this one.
/// </remarks>
public class TeamScopeIdentityTests
{
    private const string TeamA = "team-a";
    private const string TeamB = "team-b";

    private static ClaimsIdentity Identity(string teamKey, string memberKey, params string[] scopes)
    {
        var claims = new List<Claim> { new(TeamClaimTypes.TeamKey, teamKey) };
        if (memberKey != null) claims.Add(new Claim(TeamClaimTypes.MemberKey, memberKey));
        claims.AddRange(scopes.Select(s => new Claim(TeamClaimTypes.Scope, s)));
        return new ClaimsIdentity(claims, "Test");
    }

    /// <summary>A viewer on team A, administrator on team B: two identities, one per team.</summary>
    private static ClaimsPrincipal TwoTeams()
        => new([
            Identity(TeamA, "member-a", TeamScopes.Read),
            Identity(TeamB, "member-b", TeamScopes.Read, TeamScopes.MemberManage)
        ]);

    [Fact]
    public void AScopeIssuedForAnotherTeam_DoesNotCountForTheCallersTeam()
        => Assert.False(TeamScopePolicy.HasTeamScope(TwoTeams(), TeamScopes.MemberManage, TeamA));

    [Fact]
    public void AScopeIssuedForTheCallersTeam_StillCounts()
        => Assert.True(TeamScopePolicy.HasTeamScope(TwoTeams(), TeamScopes.Read, TeamA));

    /// <summary>A scope on an identity with no team key was not issued for any team.</summary>
    [Fact]
    public void AScopeOnAnIdentityWithoutATeam_DoesNotCount()
    {
        var principal = new ClaimsPrincipal([
            Identity(TeamA, null),
            new ClaimsIdentity([new Claim(TeamClaimTypes.Scope, TeamScopes.MemberManage)], "Other")
        ]);

        Assert.False(TeamScopePolicy.HasTeamScope(principal, TeamScopes.MemberManage, TeamA));
    }

    /// <summary>A member key is a membership of the team it was issued with, not of every team on the principal.</summary>
    [Fact]
    public void AMemberKeyIssuedForAnotherTeam_IsNotADirectGrant()
    {
        var principal = new ClaimsPrincipal([
            Identity(TeamA, null, TeamScopes.Manage),
            Identity(TeamB, "member-b")
        ]);

        Assert.True(TeamScopePolicy.HasTeamScope(principal, TeamScopes.Manage, TeamA));
        Assert.False(TeamScopePolicy.HasDirectTeamScope(principal, TeamScopes.Manage, TeamA));
    }

    [Fact]
    public async Task TheAuthorizer_ReadsTheScopeFromTheTeamsIdentity()
    {
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(TwoTeams()));
        var sut = new TeamAuthorizer(accessor);

        Assert.False(await sut.HasTeamScopeAsync(TeamScopes.MemberManage, TeamA));
        Assert.True(await sut.HasTeamScopeAsync(TeamScopes.Read, TeamA));
    }
}
