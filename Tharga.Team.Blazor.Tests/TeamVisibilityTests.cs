using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Tests for <see cref="TeamVisibility"/> — who may enumerate every team, and how a team's consent
/// level is reduced to the three states the UI shows.
/// </summary>
public class TeamVisibilityTests
{
    private static ClaimsPrincipal Principal(params string[] scopes)
    {
        var claims = scopes.Select(s => new Claim(TeamClaimTypes.SystemScope, s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void CanSeeAllTeams_WithTeamsRead_IsTrue()
    {
        Assert.True(TeamVisibility.CanSeeAllTeams(Principal(SystemTeamScopes.Read)));
    }

    [Fact]
    public void CanSeeAllTeams_WithoutTheScope_IsFalse()
    {
        Assert.False(TeamVisibility.CanSeeAllTeams(Principal(TeamScopes.Manage, SystemTeamScopes.Delete)));
    }

    [Fact]
    public void CanSeeAllTeams_NullPrincipal_IsFalse()
    {
        Assert.False(TeamVisibility.CanSeeAllTeams(null));
    }

    [Fact]
    public void CanSeeAllTeams_UnauthenticatedPrincipal_IsFalse()
    {
        Assert.False(TeamVisibility.CanSeeAllTeams(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private const AccessLevel HostDefault = AccessLevel.Viewer;

    [Theory]
    // No consented roles -> nothing granted, whatever level is stored.
    [InlineData(false, null, null)]
    [InlineData(false, AccessLevel.Administrator, null)]
    // Consented at an explicit level -> that level, told apart rather than banded together.
    [InlineData(true, AccessLevel.Viewer, AccessLevel.Viewer)]
    [InlineData(true, AccessLevel.User, AccessLevel.User)]
    [InlineData(true, AccessLevel.Administrator, AccessLevel.Administrator)]
    public void Resolve_ReportsTheGrantedLevel(bool hasRoles, AccessLevel? stored, AccessLevel? expected)
    {
        var roles = hasRoles ? ["Developer"] : Array.Empty<string>();

        Assert.Equal(expected, TeamVisibility.Resolve(roles, stored, HostDefault));
    }

    [Theory]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Administrator)]
    public void Resolve_LevelAbsent_UsesTheHostDefault(AccessLevel hostDefault)
    {
        Assert.Equal(hostDefault, TeamVisibility.Resolve(["Developer"], null, hostDefault));
    }

    [Fact]
    public void Resolve_NullRoles_GrantsNothing()
    {
        Assert.Null(TeamVisibility.Resolve(null, AccessLevel.Administrator, HostDefault));
    }

    [Theory]
    [InlineData(false, null, "No access", "Danger")]
    [InlineData(true, AccessLevel.Viewer, "Viewer", "Warning")]
    [InlineData(true, AccessLevel.User, "User", "Warning")]
    [InlineData(true, AccessLevel.Administrator, "Full access", "Success")]
    public void LabelAndBadgeStyle_PairTextWithTint(bool hasRoles, AccessLevel? level, string label, string badgeStyle)
    {
        var roles = hasRoles ? ["Developer"] : Array.Empty<string>();
        var consent = TeamVisibility.Resolve(roles, level, HostDefault);

        Assert.Equal(label, TeamVisibility.Label(consent));
        Assert.Equal(badgeStyle, TeamVisibility.BadgeStyle(consent));
    }

    private sealed record ConsentTeam : ITeam
    {
        public string Key => "team-1";
        public string Name => "Team";
        public string Icon => null;
        public string[] ConsentedRoles { get; init; }
        public AccessLevel? ConsentAccessLevel { get; init; }
        public TemporaryConsent TemporaryConsent { get; init; }
    }

    /// <summary>The badge shows the consent in force, so an expired temporary consent reads as the previous one.</summary>
    [Fact]
    public void ResolveForATeam_ShowsThePreviousConsent_AfterATemporaryConsentExpires()
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var team = new ConsentTeam
        {
            ConsentedRoles = ["Developer"],
            ConsentAccessLevel = AccessLevel.Administrator,
            TemporaryConsent = new TemporaryConsent { ExpiresAt = now.AddMinutes(-1), PreviousConsentedRoles = ["Developer"], PreviousConsentAccessLevel = AccessLevel.Viewer }
        };

        Assert.Equal(AccessLevel.Viewer, TeamVisibility.Resolve(team, AccessLevel.User, now));
        Assert.Equal(AccessLevel.Administrator, TeamVisibility.Resolve(team, AccessLevel.User, now.AddMinutes(-2)));
    }

    [Fact]
    public void ResolveForATeam_IsNoAccess_AfterExpiry_WithNoPreviousConsent()
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        var team = new ConsentTeam { ConsentedRoles = ["Developer"], TemporaryConsent = new TemporaryConsent { ExpiresAt = now } };

        Assert.Null(TeamVisibility.Resolve(team, AccessLevel.User, now));
    }
}
