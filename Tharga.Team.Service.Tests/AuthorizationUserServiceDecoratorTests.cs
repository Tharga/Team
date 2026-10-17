using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Authorization matrix for <see cref="AuthorizationUserServiceDecorator"/>: self-service operations
/// (current user, invitation name seeding) pass through for any caller; setting a display name is
/// allowed on the caller's own record; everything that reads or mutates other users' records —
/// enumeration, by-key reads, activity/directory writes, deletion — requires <c>users:manage</c> and
/// is denied before the inner service is touched.
/// </summary>
public class AuthorizationUserServiceDecoratorTests
{
    private static (AuthorizationUserServiceDecorator Sut, IUserService Inner) Build(ClaimsPrincipal principal)
    {
        var inner = Substitute.For<IUserService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        return (new AuthorizationUserServiceDecorator(inner, new TeamAuthorizer(accessor)), inner);
    }

    private static ClaimsPrincipal WithScopes(params string[] scopes)
        => new(new ClaimsIdentity(scopes.Select(s => new Claim(TeamClaimTypes.SystemScope, s)), "Test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    // ---- Self-service pass-through ----

    [Fact]
    public async Task GetCurrentUser_Anonymous_PassesThrough()
    {
        var (sut, inner) = Build(Anonymous());
        await sut.GetCurrentUserAsync();
        await inner.Received(1).GetCurrentUserAsync();
    }

    [Fact]
    public async Task SeedUserName_WithoutScope_PassesThrough()
    {
        var (sut, inner) = Build(Anonymous());
        await sut.SeedUserNameAsync("u-1", "Alice");
        await inner.Received(1).SeedUserNameAsync("u-1", "Alice");
    }

    // ---- SetUserNameAsync: self or users:manage ----

    [Fact]
    public async Task SetUserName_OwnRecord_WithoutScope_Delegates()
    {
        var (sut, inner) = Build(Anonymous());
        inner.GetCurrentUserAsync().Returns(new TestUser { Key = "u-me" });

        await sut.SetUserNameAsync("u-me", "New Name");

        await inner.Received(1).SetUserNameAsync("u-me", "New Name");
    }

    [Fact]
    public async Task SetUserName_OtherRecord_WithoutScope_Throws()
    {
        var (sut, inner) = Build(Anonymous());
        inner.GetCurrentUserAsync().Returns(new TestUser { Key = "u-me" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetUserNameAsync("u-other", "X"));
        await inner.DidNotReceiveWithAnyArgs().SetUserNameAsync(default, default);
    }

    [Fact]
    public async Task SetUserName_OtherRecord_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        inner.GetCurrentUserAsync().Returns(new TestUser { Key = "u-me" });

        await sut.SetUserNameAsync("u-other", "X");

        await inner.Received(1).SetUserNameAsync("u-other", "X");
    }

    // ---- Administration requires users:manage ----

    [Fact]
    public async Task GetAsync_WithScope_Streams()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));
        inner.GetAsync().Returns(new IUser[] { new TestUser { Key = "u-1" } }.ToAsyncEnumerable());

        var users = await sut.GetAsync().ToListAsync();

        Assert.Single(users);
    }

    [Fact]
    public async Task GetAsync_WithoutScope_ThrowsBeforeEnumerating()
    {
        var (sut, inner) = Build(WithScopes(TeamScopes.Manage));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await sut.GetAsync().ToListAsync());
        _ = inner.DidNotReceiveWithAnyArgs().GetAsync();
    }

    [Fact]
    public async Task GetUserByKey_WithoutScope_Throws()
    {
        var (sut, inner) = Build(Anonymous());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetUserByKeyAsync("u-1"));
        await inner.DidNotReceiveWithAnyArgs().GetUserByKeyAsync(default);
    }

    [Fact]
    public async Task GetUserByKey_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));

        await sut.GetUserByKeyAsync("u-1");

        await inner.Received(1).GetUserByKeyAsync("u-1");
    }

    [Fact]
    public async Task SetUserLastSeen_WithoutScope_Throws()
    {
        var (sut, inner) = Build(Anonymous());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetUserLastSeenAsync("u-1", DateTime.UtcNow));
        await inner.DidNotReceiveWithAnyArgs().SetUserLastSeenAsync(default, default);
    }

    [Fact]
    public async Task SetUserDirectoryId_WithoutScope_Throws()
    {
        var (sut, inner) = Build(Anonymous());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetUserDirectoryIdAsync("u-1", "oid"));
        await inner.DidNotReceiveWithAnyArgs().SetUserDirectoryIdAsync(default, default);
    }

    [Fact]
    public async Task DeleteUser_WithoutScope_Throws()
    {
        var (sut, inner) = Build(Anonymous());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.DeleteUserAsync("u-1"));
        await inner.DidNotReceiveWithAnyArgs().DeleteUserAsync(default);
    }

    [Fact]
    public async Task DeleteUser_WithScope_Delegates()
    {
        var (sut, inner) = Build(WithScopes(SystemUserScopes.Manage));

        await sut.DeleteUserAsync("u-1");

        await inner.Received(1).DeleteUserAsync("u-1");
    }

    // ---- GetTeamMemberUsersAsync: co-member projection, no scope required ----

    private sealed record TestTeam : ITeam
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public string Icon { get; init; }
    }

    private sealed record TestMember : ITeamMember
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public Invitation Invitation { get; init; }
        public DateTime? LastSeen { get; init; }
        public MembershipState? State { get; init; }
        public AccessLevel? AccessLevel { get; init; }
        public string[] TenantRoles { get; init; }
        public string[] ScopeOverrides { get; init; }
    }

    private static (AuthorizationUserServiceDecorator Sut, IUserService Inner) BuildWithTeams(
        ClaimsPrincipal principal, IUser currentUser, IEnumerable<IUser> allUsers, params (string TeamKey, string[] MemberKeys)[] teams)
    {
        var inner = Substitute.For<IUserService>();
        inner.GetCurrentUserAsync().Returns(currentUser);
        inner.GetAsync().Returns(_ => ToAsyncEnumerable(allUsers.ToArray()));

        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamsAsync().Returns(_ => ToAsyncEnumerable(teams.Select(t => (ITeam)new TestTeam { Key = t.TeamKey }).ToArray()));
        foreach (var team in teams)
        {
            var members = team.MemberKeys.Select(k => (ITeamMember)new TestMember { Key = k }).ToArray();
            teamService.GetMembersAsync(team.TeamKey).Returns(_ => ToAsyncEnumerable(members));
        }

        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));
        return (new AuthorizationUserServiceDecorator(inner, new TeamAuthorizer(accessor), () => teamService), inner);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetTeamMemberUsers_WithoutScope_ReturnsCoMembers()
    {
        var me = new TestUser { Key = "u-me", EMail = "me@test.com" };
        var mate = new TestUser { Key = "u-mate", EMail = "mate@test.com" };
        var stranger = new TestUser { Key = "u-stranger", EMail = "stranger@test.com" };
        var (sut, _) = BuildWithTeams(WithScopes(), me, [me, mate, stranger], ("t-1", ["u-me", "u-mate"]));

        var result = await sut.GetTeamMemberUsersAsync();

        Assert.Equal(["u-mate", "u-me"], result.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetTeamMemberUsers_ExcludesUsersSharingNoTeam()
    {
        var me = new TestUser { Key = "u-me" };
        var stranger = new TestUser { Key = "u-stranger" };
        var (sut, _) = BuildWithTeams(WithScopes(), me, [me, stranger], ("t-1", ["u-me"]));

        var result = await sut.GetTeamMemberUsersAsync();

        Assert.DoesNotContain(result, x => x.Key == "u-stranger");
    }

    [Fact]
    public async Task GetTeamMemberUsers_SpansEveryTeamTheCallerBelongsTo()
    {
        var me = new TestUser { Key = "u-me" };
        var a = new TestUser { Key = "u-a" };
        var b = new TestUser { Key = "u-b" };
        var (sut, _) = BuildWithTeams(WithScopes(), me, [me, a, b], ("t-1", ["u-me", "u-a"]), ("t-2", ["u-me", "u-b"]));

        var result = await sut.GetTeamMemberUsersAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetTeamMemberUsers_WithNoTeams_ReturnsCallerOnly()
    {
        var me = new TestUser { Key = "u-me" };
        var other = new TestUser { Key = "u-other" };
        var (sut, _) = BuildWithTeams(WithScopes(), me, [me, other]);

        var result = await sut.GetTeamMemberUsersAsync();

        Assert.Equal("u-me", Assert.Single(result).Key);
    }

    [Fact]
    public async Task GetTeamMemberUsers_Anonymous_Throws()
    {
        var me = new TestUser { Key = "u-me" };
        var (sut, inner) = BuildWithTeams(Anonymous(), me, [me]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamMemberUsersAsync());
        inner.DidNotReceive().GetAsync();
    }

    [Fact]
    public async Task GetTeamMemberUsers_WithoutTeamServiceFactory_ReturnsCallerOnly()
    {
        var me = new TestUser { Key = "u-me" };
        var inner = Substitute.For<IUserService>();
        inner.GetCurrentUserAsync().Returns(me);
        inner.GetAsync().Returns(_ => ToAsyncEnumerable<IUser>(me, new TestUser { Key = "u-other" }));
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(WithScopes()));
        var sut = new AuthorizationUserServiceDecorator(inner, new TeamAuthorizer(accessor));

        var result = await sut.GetTeamMemberUsersAsync();

        Assert.Equal("u-me", Assert.Single(result).Key);
    }
}
