using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Who may request, approve and deny access to a team.
/// </summary>
public class AccessRequestAuthorizationTests
{
    private const string Team = "T1";

    private static (AuthorizationTeamServiceDecorator Sut, ITeamService Inner) Build(ClaimsPrincipal principal, params string[] consentRoles)
    {
        var inner = Substitute.For<ITeamService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        var consent = consentRoles.Length == 0 ? null : new ConsentOptions { Roles = consentRoles };
        return (new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions(), consentOptions: consent), inner);
    }

    private static ClaimsPrincipal WithRoles(params string[] roles)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "dev"), .. roles.Select(r => new Claim(ClaimTypes.Role, r))], "Test"));

    private static ClaimsPrincipal Manager(bool asMember)
    {
        var claims = new List<Claim> { new(TeamClaimTypes.TeamKey, Team), new(TeamClaimTypes.Scope, TeamScopes.Manage) };
        if (asMember) claims.Add(new Claim(TeamClaimTypes.MemberKey, "member-1"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    // --- requesting ---

    [Fact]
    public async Task AConsentRoleHolder_MayRequest()
    {
        var (sut, inner) = Build(WithRoles("Developer"));

        await sut.RequestTeamAccessAsync(Team, AccessLevel.Viewer, TimeSpan.FromHours(1), null);

        await inner.Received(1).RequestTeamAccessAsync(Team, AccessLevel.Viewer, TimeSpan.FromHours(1), null);
    }

    /// <summary>Approval grants through consent, which would never reach a caller holding no consent role.</summary>
    [Fact]
    public async Task ACallerWithoutAConsentRole_IsRefused()
    {
        var (sut, inner) = Build(WithRoles("Support"));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RequestTeamAccessAsync(Team, AccessLevel.Viewer, null, null));

        Assert.Contains("Developer", ex.Message);
        await inner.DidNotReceiveWithAnyArgs().RequestTeamAccessAsync(default, default, default, default);
    }

    [Fact]
    public async Task TheConfiguredConsentRoles_AreTheOnesThatCount()
    {
        var (sut, inner) = Build(WithRoles("Support"), "Support", "Operations");

        await sut.RequestTeamAccessAsync(Team, AccessLevel.User, null, null);

        await inner.ReceivedWithAnyArgs(1).RequestTeamAccessAsync(default, default, default, default);
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_IsRefused()
    {
        var (sut, _) = Build(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Developer")])));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RequestTeamAccessAsync(Team, AccessLevel.Viewer, null, null));
    }

    /// <summary>Cancelling is checked against the requester by the inner service; nothing to gate here.</summary>
    [Fact]
    public async Task Cancelling_PassesThrough()
    {
        var (sut, inner) = Build(WithRoles());

        await sut.CancelTeamAccessRequestAsync(Team, "r1");

        await inner.Received(1).CancelTeamAccessRequestAsync(Team, "r1");
    }

    // --- approving and denying ---

    [Fact]
    public async Task AMemberManager_MayApprove()
    {
        var (sut, inner) = Build(Manager(asMember: true));

        await sut.ApproveTeamAccessRequestAsync(Team, "r1", ["Developer"]);

        await inner.Received(1).ApproveTeamAccessRequestAsync(Team, "r1", Arg.Any<string[]>());
    }

    /// <summary>A caller consented in at Administrator holds team:manage — and must not approve access through that consent.</summary>
    [Fact]
    public async Task AManagerThroughConsent_CannotApprove()
    {
        var (sut, inner) = Build(Manager(asMember: false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.ApproveTeamAccessRequestAsync(Team, "r1", ["Developer"]));
        await inner.DidNotReceiveWithAnyArgs().ApproveTeamAccessRequestAsync(default, default, default);
    }

    [Fact]
    public async Task ApprovingARoleTheHostDoesNotOffer_IsRefused()
    {
        var (sut, inner) = Build(Manager(asMember: true));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.ApproveTeamAccessRequestAsync(Team, "r1", ["Developer", "Owner"]));

        Assert.Contains("Owner", ex.Message);
        await inner.DidNotReceiveWithAnyArgs().ApproveTeamAccessRequestAsync(default, default, default);
    }

    [Fact]
    public async Task ApprovingOnAnotherTeam_IsRefused()
    {
        var (sut, _) = Build(Manager(asMember: true));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.ApproveTeamAccessRequestAsync("T2", "r1", ["Developer"]));
    }

    [Fact]
    public async Task AMemberManager_MayDeny()
    {
        var (sut, inner) = Build(Manager(asMember: true));

        await sut.DenyTeamAccessRequestAsync(Team, "r1");

        await inner.Received(1).DenyTeamAccessRequestAsync(Team, "r1");
    }

    [Fact]
    public async Task AManagerThroughConsent_CannotDeny()
    {
        var (sut, _) = Build(Manager(asMember: false));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DenyTeamAccessRequestAsync(Team, "r1"));
    }
}
