using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Granting is limited to what the granter holds (Tharga/Team#409). Whoever holds <c>apikey:manage</c> or
/// <c>member:manage</c> may administer; this asserts they cannot hand out more than they hold themselves.
/// </summary>
/// <remarks>
/// The cases are the escalations from the issue: a lower-level holder minting an Administrator key, raising a
/// member (or themselves), and reaching a key or member above them. Each is asserted refused, and the matching
/// legitimate grant asserted allowed, so a refusal that refuses everything cannot pass.
/// </remarks>
public class GrantCeilingTests
{
    private const string Team = "T1";
    private const string Read = "orders:read";
    private const string Write = "orders:write";
    private const string Approve = "orders:approve";
    private const string Special = "orders:special";

    private static ScopeRegistry Registry()
    {
        var r = new ScopeRegistry();
        r.Register(Read, AccessLevel.Viewer);
        r.Register(Write, AccessLevel.User);
        r.Register(Approve, AccessLevel.Administrator);
        r.RegisterGrantOnly(Special);
        return r;
    }

    /// <summary>A caller in <see cref="Team"/> at <paramref name="level"/> holding exactly <paramref name="scopes"/>.</summary>
    private static ClaimsPrincipal Caller(AccessLevel level, string memberKey, params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new(TeamClaimTypes.TeamKey, Team),
            new(TeamClaimTypes.AccessLevel, level.ToString()),
            new(TeamClaimTypes.MemberKey, memberKey),
        };
        claims.AddRange(scopes.Select(s => new Claim(TeamClaimTypes.Scope, s)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal User() => Caller(AccessLevel.User, "caller", Read, Write, TeamScopes.MemberManage, ApiKeyScopes.Manage);
    private static ClaimsPrincipal Admin() => Caller(AccessLevel.Administrator, "admin", Read, Write, Approve, TeamScopes.MemberManage, ApiKeyScopes.Manage);

    // --- API keys ---

    private static IApiKey Key(string id, AccessLevel? level, string[] roles = null, string[] overrides = null)
    {
        var k = Substitute.For<IApiKey>();
        k.Key.Returns(id);
        k.AccessLevel.Returns(level);
        k.Roles.Returns(roles);
        k.ScopeOverrides.Returns(overrides);
        return k;
    }

    private static async IAsyncEnumerable<IApiKey> Keys(params IApiKey[] items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    private static (ApiKeyManagementService Sut, IApiKeyAdministrationService Inner) KeyService(ClaimsPrincipal caller, params IApiKey[] existing)
    {
        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetKeysAsync(Team).Returns(_ => Keys(existing));
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(caller));
        return (new ApiKeyManagementService(inner, null, null, Registry(), null, accessor), inner);
    }

    [Fact]
    public async Task Key_Create_BelowTheCaller_IsAllowed()
    {
        var (sut, inner) = KeyService(User());

        await sut.CreateKeyAsync(Team, "k", AccessLevel.Viewer);

        await inner.Received(1).CreateKeyAsync(Team, "k", AccessLevel.Viewer, Arg.Any<string[]>(), Arg.Any<string[]>(), Arg.Any<DateTime?>(), Arg.Any<IReadOnlyList<Tag>>(), Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>The escalation in the issue: a User holding apikey:manage mints an Administrator key.</summary>
    [Fact]
    public async Task Key_Create_AtAnAdministratorLevel_ByAUser_IsRefused()
    {
        var (sut, inner) = KeyService(User());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.CreateKeyAsync(Team, "k", AccessLevel.Administrator));

        await inner.DidNotReceiveWithAnyArgs().CreateKeyAsync(default, default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Key_Create_WithAScopeOverrideTheCallerLacks_IsRefused_NamingIt()
    {
        var (sut, _) = KeyService(User());

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.CreateKeyAsync(Team, "k", AccessLevel.Viewer, scopeOverrides: [Approve]));

        Assert.Contains(Approve, ex.Message);
    }

    [Fact]
    public async Task Key_Create_WithAScopeOverrideTheCallerHolds_IsAllowed()
    {
        var (sut, inner) = KeyService(User());

        await sut.CreateKeyAsync(Team, "k", AccessLevel.Viewer, scopeOverrides: [Write]);

        await inner.ReceivedWithAnyArgs(1).CreateKeyAsync(default, default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Key_Create_AtOwner_IsRefused_ForAnyone()
    {
        var (sut, _) = KeyService(Caller(AccessLevel.Owner, "owner", Read, Write, Approve));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateKeyAsync(Team, "k", AccessLevel.Owner));
    }

    [Fact]
    public async Task Key_Create_WithAnUnregisteredOverride_IsRefused_ForAnAdministratorToo()
    {
        var (sut, _) = KeyService(Admin());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateKeyAsync(Team, "k", AccessLevel.Viewer, scopeOverrides: ["nonsense:scope"]));
    }

    /// <summary>A grant-only scope is held by no level, so an Administrator would never "hold" it; it stays grantable.</summary>
    [Fact]
    public async Task Key_Create_WithAGrantOnlyOverride_ByAnAdministrator_IsAllowed()
    {
        var (sut, inner) = KeyService(Admin());

        await sut.CreateKeyAsync(Team, "k", AccessLevel.Viewer, scopeOverrides: [Special]);

        await inner.ReceivedWithAnyArgs(1).CreateKeyAsync(default, default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Key_Create_AtAnAdministratorLevel_ByAnAdministrator_IsAllowed()
    {
        var (sut, inner) = KeyService(Admin());

        await sut.CreateKeyAsync(Team, "k", AccessLevel.Administrator);

        await inner.ReceivedWithAnyArgs(1).CreateKeyAsync(default, default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Key_SetOverrides_ToWhatTheCallerLacks_IsRefused()
    {
        var (sut, inner) = KeyService(User(), Key("k1", AccessLevel.Viewer));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetScopeOverridesAsync(Team, "k1", [Approve]));

        await inner.DidNotReceiveWithAnyArgs().SetScopeOverridesAsync(default, default, default);
    }

    /// <summary>A key above the caller is not theirs: refreshing it would hand the caller its secret.</summary>
    [Fact]
    public async Task Key_AboveTheCaller_CannotBeRefreshed_LockedDisabledOrDeleted()
    {
        var (sut, inner) = KeyService(User(), Key("big", AccessLevel.Administrator));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RefreshKeyAsync(Team, "big"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.LockKeyAsync(Team, "big"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetKeyDisabledAsync(Team, "big", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteKeyAsync(Team, "big"));

        await inner.DidNotReceiveWithAnyArgs().RefreshKeyAsync(default, default);
        await inner.DidNotReceiveWithAnyArgs().DeleteKeyAsync(default, default);
    }

    [Fact]
    public async Task Key_WithinTheCallersScopes_CanBeManaged()
    {
        var (sut, inner) = KeyService(User(), Key("small", AccessLevel.Viewer, overrides: [Write]));

        await sut.RefreshKeyAsync(Team, "small");

        await inner.Received(1).RefreshKeyAsync(Team, "small");
    }

    [Fact]
    public async Task Key_AboveTheCaller_CanBeManaged_ByAnAdministrator()
    {
        var (sut, inner) = KeyService(Admin(), Key("big", AccessLevel.Administrator));

        await sut.DeleteKeyAsync(Team, "big");

        await inner.Received(1).DeleteKeyAsync(Team, "big");
    }

    // --- members ---

    private static ITeamMember Member(string key, AccessLevel? level, string[] roles = null, string[] overrides = null)
    {
        var m = Substitute.For<ITeamMember>();
        m.Key.Returns(key);
        m.AccessLevel.Returns(level);
        m.TenantRoles.Returns(roles);
        m.ScopeOverrides.Returns(overrides);
        return m;
    }

    private static (AuthorizationTeamServiceDecorator Sut, ITeamService Inner) MemberService(ClaimsPrincipal caller, params ITeamMember[] members)
    {
        var inner = Substitute.For<ITeamService>();
        foreach (var m in members) inner.GetTeamMemberAsync(Team, m.Key).Returns(m);
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(caller));
        var registry = Registry();
        return (new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions(), registry, tenantRoleService: new TenantRoleService(inner, registry)), inner);
    }

    [Fact]
    public async Task Member_Invite_AtAnAdministratorLevel_ByAUser_IsRefused()
    {
        var (sut, inner) = MemberService(User());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.AddMemberAsync(Team, new InviteUserModel { Email = "a@b.c", AccessLevel = AccessLevel.Administrator }));

        await inner.DidNotReceiveWithAnyArgs().AddMemberAsync(default, default);
    }

    [Fact]
    public async Task Member_Invite_AtALevelTheCallerHolds_IsAllowed()
    {
        var (sut, inner) = MemberService(User());

        await sut.AddMemberAsync(Team, new InviteUserModel { Email = "a@b.c", AccessLevel = AccessLevel.Viewer });

        await inner.ReceivedWithAnyArgs(1).AddMemberAsync(default, default);
    }

    [Fact]
    public async Task Member_Raise_ToAnAdministrator_ByAUser_IsRefused()
    {
        var (sut, inner) = MemberService(User(), Member("other", AccessLevel.Viewer));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberRoleAsync(Team, "other", AccessLevel.Administrator));

        await inner.DidNotReceiveWithAnyArgs().SetMemberRoleAsync(default, default, default);
    }

    [Fact]
    public async Task Member_Scope_OverrideTheCallerLacks_IsRefused_NamingIt()
    {
        var (sut, _) = MemberService(User(), Member("other", AccessLevel.Viewer));

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberScopeOverridesAsync(Team, "other", [Approve]));

        Assert.Contains(Approve, ex.Message);
    }

    [Fact]
    public async Task Member_Scope_OverrideTheCallerHolds_IsAllowed()
    {
        var (sut, inner) = MemberService(User(), Member("other", AccessLevel.Viewer));

        await sut.SetMemberScopeOverridesAsync(Team, "other", [Write]);

        await inner.Received(1).SetMemberScopeOverridesAsync(Team, "other", Arg.Any<string[]>());
    }

    /// <summary>Roles carry scopes, so a role the caller does not fully hold is a grant above them.</summary>
    [Fact]
    public async Task Member_Roles_CarryingAScopeTheCallerLacks_IsRefused()
    {
        var registry = Registry();
        var inner = Substitute.For<ITeamService>();
        var other = Member("other", AccessLevel.Viewer);
        inner.GetTeamMemberAsync(Team, "other").Returns(other);
        inner.GetTeamCustomRolesAsync(Team).Returns(new List<TenantRoleDefinition> { new("Approver", [Approve]) });
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(User()));
        var sut = new AuthorizationTeamServiceDecorator(inner, new TeamAuthorizer(accessor), new TeamLifecycleOptions(), registry, tenantRoleService: new TenantRoleService(inner, registry));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberTenantRolesAsync(Team, "other", ["Approver"]));
    }

    /// <summary>A member above the caller is not theirs to suspend, remove or change.</summary>
    [Fact]
    public async Task Member_AboveTheCaller_CannotBeSuspended_Removed_OrChanged()
    {
        var (sut, inner) = MemberService(User(), Member("boss", AccessLevel.Administrator));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberSuspendedAsync(Team, "boss", true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RemoveMemberAsync(Team, "boss"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberRoleAsync(Team, "boss", AccessLevel.Viewer));

        await inner.DidNotReceiveWithAnyArgs().RemoveMemberAsync(default, default);
    }

    /// <summary>The issue's other escalation: raising your own row through member:manage.</summary>
    [Fact]
    public async Task Member_CannotRaiseTheirOwnRow()
    {
        var (sut, inner) = MemberService(User(), Member("caller", AccessLevel.User));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberScopeOverridesAsync(Team, "caller", [Write]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberRoleAsync(Team, "caller", AccessLevel.Viewer));

        await inner.DidNotReceiveWithAnyArgs().SetMemberScopeOverridesAsync(default, default, default);
    }

    [Fact]
    public async Task Member_AnAdministrator_CanChangeAnotherAdministrator_ButNotTheirOwnRow()
    {
        var (sut, inner) = MemberService(Admin(), Member("boss", AccessLevel.Administrator), Member("admin", AccessLevel.Administrator));

        await sut.SetMemberRoleAsync(Team, "boss", AccessLevel.User);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberRoleAsync(Team, "admin", AccessLevel.Viewer));

        await inner.Received(1).SetMemberRoleAsync(Team, "boss", AccessLevel.User);
    }

    [Fact]
    public async Task Member_AnOwner_MayChangeTheirOwnRow()
    {
        var (sut, inner) = MemberService(Caller(AccessLevel.Owner, "owner", Read, Write, Approve, TeamScopes.MemberManage), Member("owner", AccessLevel.Owner));

        await sut.SetMemberScopeOverridesAsync(Team, "owner", [Special]);

        await inner.Received(1).SetMemberScopeOverridesAsync(Team, "owner", Arg.Any<string[]>());
    }

    [Fact]
    public async Task Member_Override_OfAnUnregisteredScope_IsRefused_ForAnAdministratorToo()
    {
        var (sut, _) = MemberService(Admin(), Member("other", AccessLevel.Viewer));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetMemberScopeOverridesAsync(Team, "other", ["nonsense:scope"]));
    }

    /// <summary>A scope held in another team counts for nothing here: the ceiling reads this team's identity.</summary>
    [Fact]
    public async Task ScopesHeldInAnotherTeam_DoNotRaiseTheCeiling()
    {
        var claims = new List<Claim>
        {
            new(TeamClaimTypes.TeamKey, "T2"), new(TeamClaimTypes.AccessLevel, nameof(AccessLevel.User)),
            new(TeamClaimTypes.Scope, Write), new(TeamClaimTypes.Scope, TeamScopes.MemberManage),
        };
        var (sut, _) = MemberService(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), Member("other", AccessLevel.Viewer));

        // Refused already by the team-scope check; the point is that nothing here let it through.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetMemberScopeOverridesAsync(Team, "other", [Write]));
    }
}
