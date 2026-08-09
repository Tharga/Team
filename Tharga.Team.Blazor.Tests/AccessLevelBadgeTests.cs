using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The access-level badge beside the team selector. It replaced a consent colour dot, so it keeps that
/// audience — an oversight caller holding <c>teams:read</c>, the only one whose access varies per team —
/// and reports the caller's own level rather than the team's consent setting.
/// </summary>
public class AccessLevelBadgeTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "Test"));

    private static Claim Oversight() => new(TeamClaimTypes.SystemScope, SystemTeamScopes.Read);

    private static Claim Level(AccessLevel accessLevel) => new(TeamClaimTypes.AccessLevel, accessLevel.ToString());

    [Fact]
    public void ShouldShow_OversightCallerWithSelectedTeam_IsShown()
    {
        Assert.True(AccessLevelBadge.ShouldShow(Principal(Oversight()), hasSelectedTeam: true));
    }

    [Fact]
    public void ShouldShow_WithoutSelectedTeam_IsHidden()
    {
        Assert.False(AccessLevelBadge.ShouldShow(Principal(Oversight()), hasSelectedTeam: false));
    }

    [Fact]
    public void ShouldShow_OrdinaryMember_IsHidden()
    {
        Assert.False(AccessLevelBadge.ShouldShow(Principal(Level(AccessLevel.Administrator)), hasSelectedTeam: true));
    }

    [Fact]
    public void ShouldShow_Anonymous_IsHidden()
    {
        Assert.False(AccessLevelBadge.ShouldShow(null, hasSelectedTeam: true));
    }

    [Theory]
    [InlineData(AccessLevel.Owner)]
    [InlineData(AccessLevel.Administrator)]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.Custom)]
    public void Resolve_ReadsTheClaim(AccessLevel accessLevel)
    {
        Assert.Equal(accessLevel, AccessLevelBadge.Resolve(Principal(Level(accessLevel))));
    }

    [Fact]
    public void Resolve_NoClaim_IsNull()
    {
        Assert.Null(AccessLevelBadge.Resolve(Principal(Oversight())));
    }

    [Fact]
    public void Resolve_UnparsableClaim_IsNull()
    {
        Assert.Null(AccessLevelBadge.Resolve(Principal(new Claim(TeamClaimTypes.AccessLevel, "Nonsense"))));
    }

    [Fact]
    public void Text_NoAccessLevel_StatesThereIsNoAccess()
    {
        Assert.Equal("No access", AccessLevelBadge.Text(null, TextSet.Empty));
    }

    [Fact]
    public void Text_AccessLevel_IsTheLevelName()
    {
        Assert.Equal("Administrator", AccessLevelBadge.Text(AccessLevel.Administrator, TextSet.Empty));
    }

    [Theory]
    [InlineData(AccessLevel.Owner, "Success")]
    [InlineData(AccessLevel.Administrator, "Info")]
    [InlineData(AccessLevel.User, "Primary")]
    [InlineData(AccessLevel.Viewer, "Light")]
    [InlineData(AccessLevel.Custom, "Secondary")]
    public void Style_MapsEachLevel(AccessLevel accessLevel, string expected)
    {
        Assert.Equal(expected, AccessLevelBadge.Style(accessLevel));
    }

    [Fact]
    public void Style_NoAccess_IsDanger()
    {
        Assert.Equal("Danger", AccessLevelBadge.Style(null));
    }
}
