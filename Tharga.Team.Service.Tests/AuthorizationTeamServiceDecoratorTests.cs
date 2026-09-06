using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Authorization matrix for <see cref="AuthorizationTeamServiceDecorator"/>: each operation × caller
/// (team-admin user / team API key via scope claims, system `teams:delete` holder, unauthorized) ×
/// `AllowTeamCreation` × cross-team attempt.
/// </summary>
public class AuthorizationTeamServiceDecoratorTests
{
    private static (AuthorizationTeamServiceDecorator sut, ITeamService inner) Build(ClaimsPrincipal principal, bool allowCreation = true)
    {
        var inner = Substitute.For<ITeamService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        var sut = new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions { AllowTeamCreation = allowCreation });
        return (sut, inner);
    }

    private static ClaimsPrincipal Principal(string teamKey, params string[] scopes)
        => Principal(teamKey, scopes, []);

    /// <summary>
    /// Team scopes and system scopes are separate claim types, so a fixture has to say which it is granting.
    /// A team-level grant must not satisfy a system check, and vice versa.
    /// </summary>
    private static ClaimsPrincipal Principal(string teamKey, string[] scopes, string[] systemScopes)
    {
        var claims = new List<Claim>();
        if (teamKey != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, teamKey));
        foreach (var s in scopes) claims.Add(new Claim(TeamClaimTypes.Scope, s));
        foreach (var s in systemScopes) claims.Add(new Claim(TeamClaimTypes.SystemScope, s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    // ---- Create: authenticated + AllowTeamCreation ----
    [Fact]
    public async Task Create_Authenticated_AllowCreation_Delegates()
    {
        var (sut, inner) = Build(Principal("T1"), allowCreation: true);
        await sut.CreateTeamAsync("n");
        await inner.Received(1).CreateTeamAsync("n");
    }

    [Fact]
    public async Task Create_AllowCreationFalse_Throws()
    {
        var (sut, inner) = Build(Principal("T1"), allowCreation: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.CreateTeamAsync("n"));
        await inner.DidNotReceive().CreateTeamAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Create_Unauthenticated_Throws()
    {
        var (sut, _) = Build(Anonymous(), allowCreation: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.CreateTeamAsync("n"));
    }

    // ---- Delete: (team:manage + own team + AllowTeamCreation) OR teams:delete ----
    [Fact]
    public async Task Delete_TeamManage_OwnTeam_AllowCreation_Delegates()
    {
        var (sut, inner) = Build(Principal("T1", TeamScopes.Manage), allowCreation: true);
        await sut.DeleteTeamAsync<TestMember>("T1");
        await inner.Received(1).DeleteTeamAsync<TestMember>("T1");
    }

    [Fact]
    public async Task Delete_TeamManage_AllowCreationFalse_Throws()
    {
        var (sut, inner) = Build(Principal("T1", TeamScopes.Manage), allowCreation: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteTeamAsync<TestMember>("T1"));
        await inner.DidNotReceive().DeleteTeamAsync<TestMember>(Arg.Any<string>());
    }

    [Fact]
    public async Task Delete_SystemScope_AnyTeam_EvenAllowCreationFalse_Delegates()
    {
        var (sut, inner) = Build(Principal(null, [], [SystemTeamScopes.Delete]), allowCreation: false);
        await sut.DeleteTeamAsync<TestMember>("T-other");
        await inner.Received(1).DeleteTeamAsync<TestMember>("T-other");
    }

    [Fact]
    public async Task Delete_TeamManage_DifferentTeam_Throws()
    {
        var (sut, _) = Build(Principal("T1", TeamScopes.Manage), allowCreation: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteTeamAsync<TestMember>("T2"));
    }

    [Fact]
    public async Task Delete_NoScope_Throws()
    {
        var (sut, _) = Build(Principal("T1", TeamScopes.Read), allowCreation: true);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteTeamAsync<TestMember>("T1"));
    }

    // ---- Rename / Consent: team:manage ----
    [Fact]
    public async Task Rename_TeamManage_Delegates()
    {
        var (sut, inner) = Build(Principal("T1", TeamScopes.Manage));
        await sut.RenameTeamAsync<TestMember>("T1", "new");
        await inner.Received(1).RenameTeamAsync<TestMember>("T1", "new");
    }

    [Fact]
    public async Task Rename_MemberManageOnly_Throws()
    {
        var (sut, _) = Build(Principal("T1", TeamScopes.MemberManage));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RenameTeamAsync<TestMember>("T1", "new"));
    }

    [Fact]
    public async Task Consent_TeamManage_Delegates()
    {
        var (sut, inner) = Build(Principal("T1", TeamScopes.Manage));
        await sut.SetTeamConsentAsync("T1", ["Dev"], AccessLevel.Viewer);
        await inner.Received(1).SetTeamConsentAsync("T1", Arg.Any<string[]>(), AccessLevel.Viewer);
    }

    [Fact]
    public async Task Consent_NoScope_Throws()
    {
        var (sut, _) = Build(Principal("T1", TeamScopes.Read));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetTeamConsentAsync("T1", ["Dev"], null));
    }

    // ---- Member ops: member:manage (incl. display-name moved off team:manage) ----
    [Fact]
    public async Task RemoveMember_MemberManage_Delegates()
    {
        var (sut, inner) = Build(Principal("T1", TeamScopes.MemberManage));
        await sut.RemoveMemberAsync("T1", "U2");
        await inner.Received(1).RemoveMemberAsync("T1", "U2");
    }

    [Fact]
    public async Task SetMemberName_TeamManageOnly_Throws()
    {
        var (sut, _) = Build(Principal("T1", TeamScopes.Manage));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberNameAsync("T1", "U2", "Name"));
    }

    [Fact]
    public async Task SetMemberName_MemberManage_Delegates()
    {
        var (sut, inner) = Build(Principal("T1", TeamScopes.MemberManage));
        await sut.SetMemberNameAsync("T1", "U2", "Name");
        await inner.Received(1).SetMemberNameAsync("T1", "U2", "Name");
    }

    // ---- Leave: no scope, because the operation names no user but the caller ----
    //
    // Gating this on member:manage -- which is what removing yourself used to be -- is what stopped an
    // ordinary member leaving a team: the scope is registered at Administrator, so User and Viewer never
    // hold it. A suspended member holds nothing at all, so no scope could express the rule either.

    [Fact]
    public async Task Leave_NoScopes_Delegates()
    {
        var (sut, inner) = Build(Principal("T1"));
        await sut.LeaveTeamAsync("T1");
        await inner.Received(1).LeaveTeamAsync("T1");
    }

    [Fact]
    public async Task Leave_TeamNotSelected_Delegates()
    {
        var (sut, inner) = Build(Principal("T1"));
        await sut.LeaveTeamAsync("T2");
        await inner.Received(1).LeaveTeamAsync("T2");
    }

    /// <summary>
    /// Leaving is unscoped; removing somebody else is not. Both on the same caller, so the pair says the
    /// gate was relaxed for exactly one operation.
    /// </summary>
    [Fact]
    public async Task Leave_DoesNotRelaxRemovingAnotherMember()
    {
        var (sut, inner) = Build(Principal("T1"));

        await sut.LeaveTeamAsync("T1");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RemoveMemberAsync("T1", "U2"));

        await inner.Received(1).LeaveTeamAsync("T1");
        await inner.DidNotReceive().RemoveMemberAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    // ---- Reads pass through (no authz) ----
    [Fact]
    public void Reads_PassThrough_EvenAnonymous()
    {
        var (sut, inner) = Build(Anonymous());
        _ = sut.GetTeamsAsync();
        inner.Received(1).GetTeamsAsync();
    }
}
