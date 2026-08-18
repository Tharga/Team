using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Authorization for <c>SetOwnerAsync</c> — the operation that hands out <c>Owner</c> and takes it from
/// whoever held it.
/// </summary>
/// <remarks>
/// <b>A system grant only, with no in-team fallback, for two reasons rather than one.</b> On an ownerless
/// team no in-team caller can exist. On a team that has an owner, the in-team caller who should move
/// ownership <i>is</i> the owner, and <c>TransferOwnershipAsync</c> is already their path — so an in-team
/// fallback would let an Administrator depose the owner, which <c>SetMemberRoleAsync</c> exists to refuse.
/// <para>
/// The claim-type test below is the one that matters most: team scopes and system scopes are separate
/// claim types, and a team-level claim spelled <c>teams:set-owner</c> must not satisfy a system check. Get
/// that wrong and any team administrator who can name a scope acquires cross-tenant ownership control.
/// </para>
/// </remarks>
public class SetOwnerAuthorizationTests
{
    private static (AuthorizationTeamServiceDecorator sut, ITeamService inner) Build(ClaimsPrincipal principal)
    {
        var inner = Substitute.For<ITeamService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        var sut = new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions());
        return (sut, inner);
    }

    private static ClaimsPrincipal Principal(string teamKey, string[] scopes, string[] systemScopes)
    {
        var claims = new List<Claim>();
        if (teamKey != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, teamKey));
        foreach (var s in scopes) claims.Add(new Claim(TeamClaimTypes.Scope, s));
        foreach (var s in systemScopes) claims.Add(new Claim(TeamClaimTypes.SystemScope, s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public async Task WithTheSystemScope_Delegates()
    {
        var (sut, inner) = Build(Principal("T1", [], [SystemTeamScopes.SetOwner]));

        await sut.SetOwnerAsync<ITeamMember>("T2", "u1");

        await inner.Received(1).SetOwnerAsync<ITeamMember>("T2", "u1");
    }

    /// <summary>
    /// The caller is by definition not a member of the team they are acting on, so holding the scope for
    /// one team must not be what authorizes it — there is no team-bound half to this grant at all.
    /// </summary>
    [Fact]
    public async Task WithTheSystemScope_ActsOnATeamTheCallerIsNotAMemberOf()
    {
        var (sut, inner) = Build(Principal(teamKey: null, [], [SystemTeamScopes.SetOwner]));

        await sut.SetOwnerAsync<ITeamMember>("T9", "u1");

        await inner.Received(1).SetOwnerAsync<ITeamMember>("T9", "u1");
    }

    [Fact]
    public async Task WithoutAnyScope_Throws()
    {
        var (sut, inner) = Build(Principal("T1", [], []));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetOwnerAsync<ITeamMember>("T1", "u1"));
        await inner.DidNotReceive().SetOwnerAsync<ITeamMember>(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// <b>The escalation this guards against.</b> A team-level claim of the same name is not a system
    /// grant, and a team administrator can hold team-level scopes.
    /// </summary>
    [Fact]
    public async Task WithAnInTeamClaimOfTheSameName_Throws()
    {
        var (sut, inner) = Build(Principal("T1", [SystemTeamScopes.SetOwner], []));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetOwnerAsync<ITeamMember>("T1", "u1"));
        await inner.DidNotReceive().SetOwnerAsync<ITeamMember>(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// Holding a different system scope is not enough. Named explicitly because <c>teams:delete</c> is the
    /// grant an operator is most likely to already have, and "can destroy the team" is not the same claim
    /// as "can choose who owns it".
    /// </summary>
    [Theory]
    [InlineData("teams:delete")]
    [InlineData("teams:purge")]
    [InlineData("teams:read")]
    [InlineData("teams:manage")]
    [InlineData("users:manage")]
    public async Task WithADifferentSystemScope_Throws(string otherScope)
    {
        var (sut, inner) = Build(Principal("T1", [], [otherScope]));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetOwnerAsync<ITeamMember>("T1", "u1"));
        await inner.DidNotReceive().SetOwnerAsync<ITeamMember>(Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>
    /// The retired name authorizes nothing. A host that upgraded without remapping is refused rather than
    /// quietly still working — and <c>RetiredScopeCheck</c> is what turns that refusal into a startup error
    /// they can act on.
    /// </summary>
    [Fact]
    public async Task WithTheRetiredAssignOwnerScope_Throws()
    {
        var (sut, inner) = Build(Principal("T1", [], ["teams:assign-owner"]));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetOwnerAsync<ITeamMember>("T1", "u1"));
        await inner.DidNotReceive().SetOwnerAsync<ITeamMember>(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Anonymous_Throws()
    {
        var (sut, inner) = Build(new ClaimsPrincipal(new ClaimsIdentity()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetOwnerAsync<ITeamMember>("T1", "u1"));
        await inner.DidNotReceive().SetOwnerAsync<ITeamMember>(Arg.Any<string>(), Arg.Any<string>());
    }
}
