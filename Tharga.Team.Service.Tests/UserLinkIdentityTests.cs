using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Moving the sign-in identity of a duplicate user onto the user who keeps the teams: the duplicate is removed, the kept
/// user keeps their key, and nothing that belongs to a team is lost.
/// </summary>
public class UserLinkIdentityTests
{
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly ITeamService _teamService = Substitute.For<ITeamService>();

    private static TestUser User(string key, string email = "anna@example.com") => new() { Key = key, Identity = $"id-{key}", EMail = email };

    private UserManagementService Build() => new(_userService, _teamService);

    private void Users(params TestUser[] users)
    {
        foreach (var user in users) _userService.GetUserByKeyAsync(user.Key).Returns(user);
    }

    private void Teams(string userKey, AccessLevel level, int count)
        => _teamService.GetTeamsForUserWithAccessLevelAsync(userKey, level).Returns((IReadOnlyList<ITeam>)Enumerable.Range(0, count).Select(_ => Substitute.For<ITeam>()).ToList());

    [Fact]
    public async Task TheKeptUser_TakesTheDuplicatesIdentity_AndTheDuplicateIsRemoved()
    {
        Users(User("old"), User("new"));
        Teams("new", AccessLevel.Owner, 0);
        Teams("new", AccessLevel.Administrator, 0);
        Teams("new", AccessLevel.User, 0);
        Teams("new", AccessLevel.Viewer, 0);
        Teams("new", AccessLevel.Custom, 0);

        var result = await Build().LinkIdentityAsync("old", "new");

        Assert.Equal("id-new", result.Identity);
        Assert.Equal("id-old", result.PreviousIdentity);
        Received.InOrder(() =>
        {
            _userService.DeleteUserAsync("new");
            _userService.SetUserIdentityAsync("old", "id-new");
        });
        await _userService.DidNotReceive().DeleteUserAsync("old");
    }

    [Theory]
    [InlineData(AccessLevel.Owner)]
    [InlineData(AccessLevel.Administrator)]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.Custom)]
    public async Task ADuplicateThatBelongsToATeam_IsRefused_AndNothingChanges(AccessLevel level)
    {
        Users(User("old"), User("new"));
        foreach (var other in Enum.GetValues<AccessLevel>()) Teams("new", other, other == level ? 1 : 0);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Build().LinkIdentityAsync("old", "new"));

        Assert.Contains("1 team", error.Message);
        await _userService.DidNotReceiveWithAnyArgs().DeleteUserAsync(default);
        await _userService.DidNotReceiveWithAnyArgs().SetUserIdentityAsync(default, default);
    }

    [Fact]
    public async Task ALinkToItself_IsRefused()
    {
        Users(User("old"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Build().LinkIdentityAsync("old", "old"));
        await _userService.DidNotReceiveWithAnyArgs().SetUserIdentityAsync(default, default);
    }

    [Theory]
    [InlineData("gone", "new")]
    [InlineData("old", "gone")]
    public async Task AnUnknownUser_IsRefused(string keep, string duplicate)
    {
        Users(User("old"), User("new"));
        _userService.GetUserByKeyAsync("gone").Returns((IUser)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Build().LinkIdentityAsync(keep, duplicate));
        await _userService.DidNotReceiveWithAnyArgs().DeleteUserAsync(default);
    }

    [Fact]
    public async Task WithoutTheScope_NothingIsDelegated()
    {
        var inner = Substitute.For<IUserManagementService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.SystemScope, TeamScopes.Manage)], "Test"))));
        var sut = new AuthorizationUserManagementServiceDecorator(inner, new TeamAuthorizer(accessor));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.LinkIdentityAsync("old", "new"));
        await inner.DidNotReceiveWithAnyArgs().LinkIdentityAsync(default, default);
    }

    [Fact]
    public async Task WithTheScope_ItIsDelegated()
    {
        var inner = Substitute.For<IUserManagementService>();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.SystemScope, SystemUserScopes.Manage)], "Test"))));
        var sut = new AuthorizationUserManagementServiceDecorator(inner, new TeamAuthorizer(accessor));

        await sut.LinkIdentityAsync("old", "new");

        await inner.Received(1).LinkIdentityAsync("old", "new", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLink_IsAudited_WithBothUsersAndBothIdentities()
    {
        var inner = Substitute.For<IUserManagementService>();
        inner.LinkIdentityAsync("old", "new", Arg.Any<CancellationToken>()).Returns(new UserLinkResult("id-new", "id-old"));
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        var sut = new AuditingUserManagementServiceDecorator(inner, logger, Substitute.For<IHttpContextAccessor>());

        await sut.LinkIdentityAsync("old", "new");

        var entry = Assert.Single(backend.Entries);
        Assert.Equal("link-identity", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("old", entry.Metadata[AuditMetadataKeys.UserKey]);
        Assert.Equal("new", entry.Metadata[AuditMetadataKeys.LinkedUserKey]);
        Assert.Equal("id-new", entry.Metadata["identity.new"]);
        Assert.Equal("id-old", entry.Metadata["identity.previous"]);
    }

    [Fact]
    public async Task AFailedLink_IsAuditedAsAFailure_AndRethrown()
    {
        var inner = Substitute.For<IUserManagementService>();
        inner.LinkIdentityAsync("old", "new", Arg.Any<CancellationToken>()).Returns<Task<UserLinkResult>>(_ => throw new InvalidOperationException("boom"));
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        var sut = new AuditingUserManagementServiceDecorator(inner, logger, Substitute.For<IHttpContextAccessor>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LinkIdentityAsync("old", "new"));

        Assert.False(Assert.Single(backend.Entries).Success);
    }

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }
}
