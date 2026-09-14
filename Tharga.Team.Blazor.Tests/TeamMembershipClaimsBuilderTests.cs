using System.Security.Claims;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Direct coverage of the shared membership/consent claim builder — notably the consent-grant path, which
/// the transformation and revalidator tests exercise only in its "no access" form.
/// </summary>
public class TeamMembershipClaimsBuilderTests
{
    private const string TeamKey = "team-1";
    private const string UserKey = "user-1";

    private readonly Mock<ITeamService> _teamService = new();
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IScopeRegistry> _scopeRegistry = new();
    private readonly Mock<IOptions<ThargaBlazorOptions>> _options = new();

    public TeamMembershipClaimsBuilderTests()
    {
        _options.Setup(o => o.Value).Returns(new ThargaBlazorOptions());
        _userService.Setup(u => u.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Mock.Of<IUser>(u => u.Key == UserKey));
    }

    private TeamMembershipClaimsBuilder CreateSut() =>
        new(_teamService.Object, _userService.Object, _options.Object, _scopeRegistry.Object);

    private static async IAsyncEnumerable<ITeam> Async(params ITeam[] items)
    {
        await Task.CompletedTask;
        foreach (var item in items) yield return item;
    }

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "TestAuth"));

    [Fact]
    public async Task NoTeamKey_ReturnsEmpty()
    {
        var claims = await CreateSut().BuildAsync(PrincipalWithRoles("Support"), "");

        Assert.Empty(claims);
    }

    [Fact]
    public async Task NonMemberWithConsentedRole_GetsConsentAccessAtTheTeamsLevel()
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync((ITeamMember)null);
        _teamService.Setup(t => t.GetConsentedTeamsAsync(It.Is<string[]>(r => r.Contains("Support"))))
            .Returns(Async(Mock.Of<ITeam>(t => t.Key == TeamKey && t.ConsentedRoles == new[] { "Support" } && t.ConsentAccessLevel == AccessLevel.Viewer)));
        _scopeRegistry.Setup(s => s.GetEffectiveScopes(AccessLevel.Viewer, It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { "team:read" });

        var claims = await CreateSut().BuildAsync(PrincipalWithRoles("Support"), TeamKey);

        Assert.Contains(claims, c => c.Type == TeamClaimTypes.TeamKey && c.Value == TeamKey);
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == Roles.TeamMember);
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == "TeamViewer");
        Assert.Contains(claims, c => c.Type == TeamClaimTypes.AccessLevel && c.Value == "Viewer");
        Assert.Contains(claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "team:read");
    }

    [Fact]
    public async Task NonMemberWithoutAConsentedTeam_ReturnsEmpty()
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync((ITeamMember)null);
        _teamService.Setup(t => t.GetConsentedTeamsAsync(It.IsAny<string[]>())).Returns(Async());

        var claims = await CreateSut().BuildAsync(PrincipalWithRoles("Support"), TeamKey);

        Assert.Empty(claims);
    }

    private static ITeamMember Suspended(AccessLevel accessLevel = AccessLevel.Administrator)
        => Mock.Of<ITeamMember>(m =>
            m.Key == UserKey &&
            m.AccessLevel == accessLevel &&
            m.SuspendedAt == new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc));

    private static ITeamMember Active(AccessLevel accessLevel = AccessLevel.Administrator)
        => Mock.Of<ITeamMember>(m => m.Key == UserKey && m.AccessLevel == accessLevel);

    /// <summary>
    /// The whole of the suspension feature rests here. This builder is the only thing that decides what a
    /// member may do, and it never consulted membership state before — it granted the access level's full
    /// scope set the moment a member came back from the store.
    /// </summary>
    [Fact]
    public async Task ASuspendedMember_GetsNothing()
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync(Suspended());
        _scopeRegistry.Setup(s => s.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { "team:read", "member:manage" });

        var claims = await CreateSut().BuildAsync(PrincipalWithRoles("Support"), TeamKey);

        Assert.Empty(claims);
    }

    /// <summary>
    /// <b>No <c>TeamKey</c> claim either.</b> Filtering only the scopes would leave the member looking
    /// like they are "in" the team to every service-layer check that reads that claim — a subtler and
    /// worse state than either being in or out.
    /// </summary>
    [Fact]
    public async Task ASuspendedMember_IsNotEvenMarkedAsBeingInTheTeam()
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync(Suspended());

        var claims = await CreateSut().BuildAsync(PrincipalWithRoles("Support"), TeamKey);

        Assert.DoesNotContain(claims, c => c.Type == TeamClaimTypes.TeamKey);
        Assert.DoesNotContain(claims, c => c.Type == ClaimTypes.Role && c.Value == Roles.TeamMember);
    }

    /// <summary>
    /// The one that is easy to get wrong. Consent grants access by global role rather than by membership,
    /// so a suspended member who happens to hold a consented role would walk straight back in through the
    /// non-member path. Suspension is the more specific and more recent decision, so it wins.
    /// </summary>
    [Fact]
    public async Task ASuspendedMember_DoesNotFallThroughToConsent()
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync(Suspended());
        _teamService.Setup(t => t.GetConsentedTeamsAsync(It.IsAny<string[]>()))
            .Returns(Async(Mock.Of<ITeam>(t => t.Key == TeamKey && t.ConsentAccessLevel == AccessLevel.Administrator)));
        _scopeRegistry.Setup(s => s.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { "team:read" });

        var claims = await CreateSut().BuildAsync(PrincipalWithRoles("Support"), TeamKey);

        Assert.Empty(claims);
        _teamService.Verify(t => t.GetConsentedTeamsAsync(It.IsAny<string[]>()), Times.Never);
    }

    /// <summary>
    /// Suspension is not a one-way latch, and an Owner is not exempt from the check here — the refusal to
    /// suspend an owner lives in the service, so if one somehow exists the builder must still honour it.
    /// </summary>
    [Fact]
    public async Task ASuspendedOwner_AlsoGetsNothing()
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync(Suspended(AccessLevel.Owner));
        _scopeRegistry.Setup(s => s.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { "team:read" });

        var claims = await CreateSut().BuildAsync(PrincipalWithRoles("Support"), TeamKey);

        Assert.Empty(claims);
    }

    /// <summary>The other direction: an ordinary member is unaffected, so the check has not broken everyone.</summary>
    [Fact]
    public async Task AnActiveMember_IsUnaffected()
    {
        _teamService.Setup(t => t.GetTeamMemberAsync(TeamKey, UserKey)).ReturnsAsync(Active());
        _scopeRegistry.Setup(s => s.GetEffectiveScopes(It.IsAny<AccessLevel>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(new[] { "team:read" });

        var claims = await CreateSut().BuildAsync(PrincipalWithRoles("Support"), TeamKey);

        Assert.Contains(claims, c => c.Type == TeamClaimTypes.TeamKey && c.Value == TeamKey);
        Assert.Contains(claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "team:read");
    }
}
