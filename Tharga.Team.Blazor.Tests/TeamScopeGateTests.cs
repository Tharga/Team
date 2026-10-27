using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A component must not offer what the server will refuse. An in-team scope is issued for the selected
/// team only, so "holds the claim" and "holds it here" are different questions — conflating them is what
/// rendered API key management for a caller with no access to the selected team.
/// </summary>
public class TeamScopeGateTests
{
    private static ClaimsPrincipal Principal(string teamKey, params string[] scopes)
    {
        var claims = new List<Claim>();
        if (teamKey != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, teamKey));
        foreach (var scope in scopes) claims.Add(new Claim(TeamClaimTypes.Scope, scope));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal SystemPrincipal(params string[] systemScopes)
    {
        var claims = systemScopes.Select(s => new Claim(TeamClaimTypes.SystemScope, s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void HasTeamScope_ForTheIssuedTeam_IsTrue()
    {
        Assert.True(TeamScopeGate.HasTeamScope(Principal("team-a", ApiKeyScopes.Manage), ApiKeyScopes.Manage, "team-a"));
    }

    [Fact]
    public void HasTeamScope_ForAnotherTeam_IsFalse()
    {
        Assert.False(TeamScopeGate.HasTeamScope(Principal("team-a", ApiKeyScopes.Manage), ApiKeyScopes.Manage, "team-b"));
    }

    [Fact]
    public void HasTeamScope_WithoutTheScope_IsFalse()
    {
        Assert.False(TeamScopeGate.HasTeamScope(Principal("team-a"), ApiKeyScopes.Manage, "team-a"));
    }

    [Fact]
    public void HasTeamScope_WithNoTeamClaim_IsFalse()
    {
        Assert.False(TeamScopeGate.HasTeamScope(Principal(null, ApiKeyScopes.Manage), ApiKeyScopes.Manage, "team-a"));
    }

    /// <summary>A scope on an identity issued for another team does not count for the caller's team.</summary>
    [Fact]
    public void HasTeamScope_FromAnIdentityForAnotherTeam_IsFalse()
    {
        var principal = new ClaimsPrincipal([
            new ClaimsIdentity([new Claim(TeamClaimTypes.TeamKey, "team-a")], "Test"),
            new ClaimsIdentity([new Claim(TeamClaimTypes.TeamKey, "team-b"), new Claim(TeamClaimTypes.Scope, ApiKeyScopes.Manage)], "Other")
        ]);

        Assert.False(TeamScopeGate.HasTeamScope(principal, ApiKeyScopes.Manage, "team-a"));
    }

    [Fact]
    public void HasTeamScope_ForNoTeam_IsFalse()
    {
        Assert.False(TeamScopeGate.HasTeamScope(Principal("team-a", ApiKeyScopes.Manage), ApiKeyScopes.Manage, null));
    }

    [Fact]
    public void HasTeamScope_Anonymous_IsFalse()
    {
        Assert.False(TeamScopeGate.HasTeamScope(null, ApiKeyScopes.Manage, "team-a"));
    }

    [Fact]
    public void HasSystemScope_WithTheScope_IsTrue()
    {
        Assert.True(TeamScopeGate.HasSystemScope(SystemPrincipal(SystemUserScopes.Manage), SystemUserScopes.Manage));
    }

    [Fact]
    public void HasSystemScope_Anonymous_IsFalse()
    {
        Assert.False(TeamScopeGate.HasSystemScope(null, SystemUserScopes.Manage));
    }
}
