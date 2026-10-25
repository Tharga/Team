using System.Security.Claims;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The core of the #127 fix: given a frozen circuit principal, recompute the caller's team claims and
/// produce a refreshed principal only when they changed — preserving system scopes and app roles, and
/// failing open on a recompute error.
/// </summary>
public class TeamClaimRevalidatorTests
{
    private const string TeamKey = "team-1";
    private const string UserKey = "user-1";

    private readonly Mock<ITeamService> _teamService = new();
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IScopeRegistry> _scopeRegistry = new();
    private readonly Mock<ISystemRoleRegistry> _systemRoleRegistry = new();
    private readonly Mock<IOptions<ThargaBlazorOptions>> _options = new();

    public TeamClaimRevalidatorTests()
    {
        _options.Setup(o => o.Value).Returns(new ThargaBlazorOptions());
        _userService.Setup(u => u.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>(), false))
            .ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));
        _teamService.Setup(t => t.GetConsentedTeamsAsync(It.IsAny<string[]>()))
            .Returns(AsyncEnumerable.Empty<ITeam>());
    }

    private TeamClaimRevalidator CreateSut() =>
        new(new TeamMembershipClaimsBuilder(_teamService.Object, _userService.Object, _options.Object, _scopeRegistry.Object),
            _systemRoleRegistry.Object);

    private void MemberIs(AccessLevel accessLevel, params string[] scopes)
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey))
            .ReturnsAsync(Mock.Of<ITeamMember>(m =>
                m.AccessLevel == accessLevel &&
                m.TenantRoles == Array.Empty<string>() &&
                m.ScopeOverrides == Array.Empty<string>()));
        _scopeRegistry.Setup(s => s.GetEffectiveScopes(accessLevel, It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(scopes);
    }

    private void NotAMember()
        => _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync((ITeamMember)null);

    /// <summary>A frozen circuit principal for a member at <paramref name="accessLevel"/> with the given team scopes.</summary>
    private static ClaimsPrincipal FrozenMember(AccessLevel accessLevel, string[] teamScopes, params Claim[] extra)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "testuser"),
            new(Constants.TeamKeyCookie, TeamKey), // team_id marker — how the revalidator finds the selected team
            new(TeamClaimTypes.TeamKey, TeamKey),
            new(ClaimTypes.Role, Roles.TeamMember),
            new(ClaimTypes.Role, $"Team{accessLevel}"),
            new(TeamClaimTypes.AccessLevel, accessLevel.ToString()),
        };
        claims.AddRange(teamScopes.Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    /// <summary>The refresh runs every interval on an idle tab too, which is not the user doing anything, so it records no activity.</summary>
    [Fact]
    public async Task TheRefresh_DoesNotCountAsTheUserBeingActive()
    {
        MemberIs(AccessLevel.Administrator, "team:read");

        await CreateSut().TryRefreshAsync(FrozenMember(AccessLevel.Administrator, ["team:read"]));

        _userService.Verify(u => u.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>(), false), Times.Once);
        _userService.Verify(u => u.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    [Fact]
    public async Task Unchanged_ReturnsNull()
    {
        MemberIs(AccessLevel.Administrator, "team:read", "team:manage");
        var principal = FrozenMember(AccessLevel.Administrator, ["team:read", "team:manage"]);

        var result = await CreateSut().TryRefreshAsync(principal);

        Assert.Null(result);
    }

    [Fact]
    public async Task MemberRemoved_DropsAllTeamClaims_KeepsIdentity()
    {
        NotAMember(); // removed, and no consent
        var principal = FrozenMember(AccessLevel.Administrator, ["team:read", "team:manage"],
            new Claim(ClaimTypes.Role, "Support"));

        var result = await CreateSut().TryRefreshAsync(principal);

        Assert.NotNull(result);
        Assert.DoesNotContain(result.Claims, c => c.Type == TeamClaimTypes.TeamKey);
        Assert.DoesNotContain(result.Claims, c => c.Type == TeamClaimTypes.AccessLevel);
        Assert.DoesNotContain(result.Claims, c => c.Type == ClaimTypes.Role && c.Value == Roles.TeamMember);
        Assert.DoesNotContain(result.Claims, c => c.Type == TeamClaimTypes.Scope);
        // Identity, the team_id marker, and app roles survive.
        Assert.Equal("testuser", result.Identity!.Name);
        Assert.Contains(result.Claims, c => c.Type == Constants.TeamKeyCookie && c.Value == TeamKey);
        Assert.Contains(result.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Support");
    }

    [Fact]
    public async Task AccessDowngraded_LowersLevelAndDropsElevatedScopes()
    {
        MemberIs(AccessLevel.Viewer, "team:read");
        var principal = FrozenMember(AccessLevel.Administrator, ["team:read", "team:manage"]);

        var result = await CreateSut().TryRefreshAsync(principal);

        Assert.NotNull(result);
        Assert.Contains(result.Claims, c => c.Type == TeamClaimTypes.AccessLevel && c.Value == "Viewer");
        Assert.Contains(result.Claims, c => c.Type == ClaimTypes.Role && c.Value == "TeamViewer");
        Assert.DoesNotContain(result.Claims, c => c.Type == ClaimTypes.Role && c.Value == "TeamAdministrator");
        Assert.Contains(result.Claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "team:read");
        Assert.DoesNotContain(result.Claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "team:manage");
    }

    [Fact]
    public async Task SystemScopes_ArePreserved_AcrossADowngrade()
    {
        // "SysAdmin" app role grants the team-independent users:manage system scope.
        _systemRoleRegistry.Setup(r => r.GetScopesForRoles(It.Is<IEnumerable<string>>(roles => roles.Contains("SysAdmin"))))
            .Returns(new[] { "users:manage" });
        MemberIs(AccessLevel.Viewer, "team:read");

        var principal = FrozenMember(AccessLevel.Administrator, ["team:read", "team:manage", "users:manage"],
            new Claim(ClaimTypes.Role, "SysAdmin"));

        var result = await CreateSut().TryRefreshAsync(principal);

        Assert.NotNull(result);
        Assert.Contains(result.Claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "users:manage"); // system scope kept
        Assert.DoesNotContain(result.Claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "team:manage"); // team scope dropped
        Assert.Contains(result.Claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "team:read");
    }

    [Fact]
    public async Task ConsentRevoked_NonMemberLosesConsentClaims_KeepsAppRole()
    {
        // Frozen as a non-member acting through consent (Viewer). Consent is now gone: not a member, no consented team.
        NotAMember();
        var principal = FrozenMember(AccessLevel.Viewer, ["team:read"], new Claim(ClaimTypes.Role, "Support"));

        var result = await CreateSut().TryRefreshAsync(principal);

        Assert.NotNull(result);
        Assert.DoesNotContain(result.Claims, c => c.Type == TeamClaimTypes.TeamKey);
        Assert.DoesNotContain(result.Claims, c => c.Type == TeamClaimTypes.Scope);
        Assert.Contains(result.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Support");
    }

    [Fact]
    public async Task NoTeamSelected_ReturnsNull_WithoutHittingTheService()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "testuser")], "TestAuth")); // no team_id marker

        var result = await CreateSut().TryRefreshAsync(principal);

        Assert.Null(result);
        _teamService.Verify(t => t.GetTeamMemberAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Unauthenticated_ReturnsNull()
    {
        var result = await CreateSut().TryRefreshAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(result);
    }

    [Fact]
    public async Task RecomputeThrows_FailsOpen_ReturnsNull()
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var principal = FrozenMember(AccessLevel.Administrator, ["team:read", "team:manage"]);

        var result = await CreateSut().TryRefreshAsync(principal);

        Assert.Null(result); // keep current claims; do not sign the user out on a transient error
    }

    [Fact]
    public async Task AccessUpgraded_AddsElevatedScopesAndRole()
    {
        MemberIs(AccessLevel.Administrator, "team:read", "team:manage");
        var principal = FrozenMember(AccessLevel.Viewer, ["team:read"]);

        var result = await CreateSut().TryRefreshAsync(principal);

        Assert.NotNull(result);
        Assert.Contains(result.Claims, c => c.Type == TeamClaimTypes.AccessLevel && c.Value == "Administrator");
        Assert.Contains(result.Claims, c => c.Type == ClaimTypes.Role && c.Value == "TeamAdministrator");
        Assert.DoesNotContain(result.Claims, c => c.Type == ClaimTypes.Role && c.Value == "TeamViewer");
        Assert.Contains(result.Claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "team:manage");
    }
}
