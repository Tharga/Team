using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Tharga.Mcp;
using Tharga.Team.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp.Tests;

public class TeamUserResourceProviderTests
{
    private readonly IUserService _userService = Substitute.For<IUserService>();
    private readonly ITeamManagementService _teamService = Substitute.For<ITeamManagementService>();
    private readonly ITeamDirectoryService _teamDirectory = Substitute.For<ITeamDirectoryService>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

    private TeamMcpContext MakeContext(string userId)
        => TestMcpContextFactory.Create(userId: userId, scope: McpScope.User);

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    private TeamUserResourceProvider CreateSut()
        => new(_userService, _teamService, _teamDirectory, _httpContextAccessor);

    [Fact]
    public async Task ListResourcesAsync_AnonymousContext_ReturnsEmpty()
    {
        var sut = CreateSut();

        var result = await sut.ListResourcesAsync(MakeContext(userId: null), TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListResourcesAsync_AuthenticatedContext_ReturnsMeResource()
    {
        var sut = CreateSut();

        var result = await sut.ListResourcesAsync(MakeContext(userId: "u-1"), TestContext.Current.CancellationToken);

        var descriptor = Assert.Single(result);
        Assert.Equal(TeamUserResourceProvider.MeUri, descriptor.Uri);
        Assert.Equal("application/json", descriptor.MimeType);
    }

    [Fact]
    public async Task ReadResourceAsync_UnknownUri_Throws()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ReadResourceAsync("team://nope", MakeContext("u-1"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadResourceAsync_NoCurrentUser_Throws()
    {
        _userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns((IUser)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.ReadResourceAsync(TeamUserResourceProvider.MeUri, MakeContext("u-1"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadResourceAsync_ReturnsUserAndMemberships()
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns("u-alice");
        user.Identity.Returns("alice@example.com");
        user.Name.Returns("Alice");
        user.EMail.Returns("alice@example.com");
        _userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);

        var team1 = Substitute.For<ITeam>();
        team1.Key.Returns("T-1");
        team1.Name.Returns("First");
        var team2 = Substitute.For<ITeam>();
        team2.Key.Returns("T-2");
        team2.Name.Returns("Second");
        _teamDirectory.GetTeamsAsync().Returns(ToAsyncEnumerable(team1, team2));

        var aliceInT1 = Substitute.For<ITeamMember>();
        aliceInT1.Key.Returns("u-alice");
        aliceInT1.AccessLevel.Returns(AccessLevel.Owner);
        aliceInT1.State.Returns(MembershipState.Member);
        var bobInT1 = Substitute.For<ITeamMember>();
        bobInT1.Key.Returns("u-bob");
        _teamService.GetMembersAsync("T-1").Returns(ToAsyncEnumerable(bobInT1, aliceInT1));

        var aliceInT2 = Substitute.For<ITeamMember>();
        aliceInT2.Key.Returns("u-alice");
        aliceInT2.AccessLevel.Returns(AccessLevel.User);
        aliceInT2.State.Returns(MembershipState.Member);
        _teamService.GetMembersAsync("T-2").Returns(ToAsyncEnumerable(aliceInT2));

        var sut = CreateSut();
        var content = await sut.ReadResourceAsync(TeamUserResourceProvider.MeUri, MakeContext("u-alice"), TestContext.Current.CancellationToken);

        Assert.Equal(TeamUserResourceProvider.MeUri, content.Uri);
        Assert.Equal("application/json", content.MimeType);

        using var doc = JsonDocument.Parse(content.Text);
        var root = doc.RootElement;
        var userJson = root.GetProperty("user");
        Assert.Equal("u-alice", userJson.GetProperty("key").GetString());
        Assert.Equal("Alice", userJson.GetProperty("name").GetString());
        Assert.Equal("alice@example.com", userJson.GetProperty("email").GetString());

        var memberships = root.GetProperty("memberships");
        Assert.Equal(2, memberships.GetArrayLength());
        var first = memberships[0];
        Assert.Equal("T-1", first.GetProperty("teamKey").GetString());
        Assert.Equal("First", first.GetProperty("teamName").GetString());
        Assert.Equal((int)AccessLevel.Owner, first.GetProperty("accessLevel").GetInt32());
    }
}
