namespace Tharga.Team.Service.Tests;

/// <summary>
/// A member whose access level was never set is granted nothing (4.0). Before it, the unset level was the
/// enum's zero value, <see cref="AccessLevel.Owner"/>.
/// </summary>
/// <remarks>
/// Each site is asserted to fail closed <i>explicitly</i>. The lifted comparison operators already answer
/// false for null, but by accident — and an accident that happens to be correct is not a guarantee.
/// </remarks>
public class UnsetAccessLevelTests
{
    private const string Team = "team-1";

    private static ScopeRegistry Registry()
    {
        var registry = new ScopeRegistry();
        registry.Register("data:read", AccessLevel.Viewer);
        registry.Register("data:write", AccessLevel.User);
        return registry;
    }

    // ---- The registry ----

    [Fact]
    public void Registry_ANullLevel_GrantsNoScopes()
    {
        Assert.Empty(Registry().GetScopesForAccessLevel(null));
    }

    [Fact]
    public void Registry_ANullLevel_GrantsNothing_EvenWithOverrides()
    {
        Assert.Empty(Registry().GetEffectiveScopes(null, [], ["data:write"]));
    }

    [Fact]
    public async Task TenantRoles_ANullLevel_GrantsNothing_EvenWithACustomRole()
    {
        var teams = Substitute.For<ITeamService>();
        teams.GetTeamCustomRolesAsync(Team).Returns([new TenantRoleDefinition("Registrar", ["data:write"])]);
        var sut = new TenantRoleService(teams, Registry());

        Assert.Empty(await sut.GetEffectiveScopesAsync(Team, null, ["Registrar"], ["data:read"]));
    }

    // ---- The grant ----

    [Fact]
    public async Task Grant_AMemberWithNoLevel_ResolvesToNoGrant()
    {
        var member = new TestMember { Key = "user-1", State = MembershipState.Member, ScopeOverrides = ["data:write"] };

        var grant = await new TeamGrantResolver(Substitute.For<ITeamService>(), Registry()).ResolveFromMemberAsync(Team, member);

        Assert.Null(grant);
    }

    // ---- Privilege ----

    [Theory]
    [InlineData(AccessLevel.Owner)]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.Custom)]
    public void Privilege_NullIsBelowEveryLevel(AccessLevel level)
    {
        Assert.True(AccessLevelPrivilege.IsNoMorePrivilegedThan(null, level));
        Assert.False(AccessLevelPrivilege.IsNoMorePrivilegedThan(level, null));
    }

    [Theory]
    [InlineData(AccessLevel.Owner)]
    [InlineData(AccessLevel.Custom)]
    public void Privilege_ClampingAgainstNoLevel_GivesNoLevel(AccessLevel candidate)
    {
        Assert.Null(AccessLevelPrivilege.Clamp(candidate, (AccessLevel?)null));
        Assert.Null(AccessLevelPrivilege.Clamp((AccessLevel?)null, candidate));
    }

    // ---- The last-administrator guard ----

    [Fact]
    public async Task LastAdministrator_AMemberWithNoLevel_DoesNotCountAsAnotherAdministrator()
    {
        var users = Substitute.For<IUserService>();
        var me = Substitute.For<IUser>();
        me.Key.Returns("user-2");
        users.GetCurrentUserAsync().Returns(me);
        var sut = new TestTeamService(users);
        sut.AddTeam(Team, "Test Team",
            new TestMember { Key = "user-2", AccessLevel = AccessLevel.Administrator, State = MembershipState.Member },
            new TestMember { Key = "user-3", State = MembershipState.Member });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LeaveTeamAsync(Team));

        Assert.Contains("last administrator", ex.Message);
    }
}
