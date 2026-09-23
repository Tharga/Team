using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// What a component sees of access requests through the facades, and which roles approval consents.
/// </summary>
public class TeamAccessRequestFacadeTests
{
    private const string Me = "me";

    private static TeamAccessRequest Request(string id, string requester, TeamAccessRequestStatus status = TeamAccessRequestStatus.Pending, int minutesAgo = 0) => new()
    {
        Id = id,
        RequesterKey = requester,
        AccessLevel = AccessLevel.User,
        RequestedAt = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo),
        Status = status
    };

    private static TestTeam Team(string key, AccessLevel? myLevel, params TeamAccessRequest[] requests) => new()
    {
        Key = key,
        Name = $"Team {key}",
        Members = myLevel == null ? [] : [new TestMember { Key = Me, AccessLevel = myLevel.Value, State = MembershipState.Member }],
        AccessRequests = requests
    };

    private static (TeamManagementService<TestMember> Sut, ITeamService Inner) Build(params TestTeam[] teams)
    {
        var inner = Substitute.For<ITeamService>();
        inner.GetTeamsAsync<TestMember>().Returns(teams.Where(t => t.Members.Length > 0).Cast<ITeam<TestMember>>().ToAsyncEnumerable());
        inner.GetAllTeamsAsync().Returns(teams.Cast<ITeam>().ToAsyncEnumerable());
        foreach (var team in teams)
        {
            inner.GetTeamByKeyAsync(team.Key).Returns(team);
            inner.GetTeamMemberAsync(team.Key, Me).Returns(team.Members.FirstOrDefault());
        }

        var user = Substitute.For<IUser>();
        user.Key.Returns(Me);
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync().Returns(user);

        var registry = new ScopeRegistry();
        registry.Register(TeamScopes.Read, AccessLevel.Viewer);
        registry.Register(TeamScopes.Manage, AccessLevel.Administrator);

        var consent = Options.Create(new ConsentOptions { Roles = ["Developer", "Support"] });
        return (new TeamManagementService<TestMember>(inner, userService, registry, null, null, consent), inner);
    }

    // --- the manager's read ---

    [Fact]
    public async Task AMemberManager_ReadsTheTeamsRequests_NewestFirst()
    {
        var (sut, _) = Build(Team("T1", AccessLevel.Administrator, Request("old", "dev", minutesAgo: 10), Request("new", "dev")));

        var requests = await sut.GetAccessRequestsAsync("T1");

        Assert.Equal(["new", "old"], requests.Select(x => x.Id));
    }

    /// <summary>The requests carry who asked and why; a member who cannot decide them does not read them.</summary>
    [Fact]
    public async Task AMemberWithoutManage_CannotRead()
    {
        var (sut, _) = Build(Team("T1", AccessLevel.User, Request("r1", "dev")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetAccessRequestsAsync("T1"));
    }

    [Fact]
    public async Task ANonMember_CannotRead()
    {
        var (sut, _) = Build(Team("T1", myLevel: null, Request("r1", "dev")));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetAccessRequestsAsync("T1"));
    }

    // --- approval ---

    /// <summary>The configured consent roles — the same set the consent selector consents — not something the caller supplies.</summary>
    [Fact]
    public async Task Approving_ConsentsTheConfiguredConsentRoles()
    {
        var (sut, inner) = Build(Team("T1", AccessLevel.Owner));

        await sut.ApproveTeamAccessRequestAsync("T1", "r1");

        await inner.Received(1).ApproveTeamAccessRequestAsync("T1", "r1", Arg.Is<string[]>(r => r.SequenceEqual(new[] { "Developer", "Support" })));
    }

    // --- my requests ---

    [Fact]
    public async Task MyRequests_AreOnlyMine_AcrossTeams()
    {
        var (sut, _) = Build(
            Team("T1", null, Request("mine-1", Me, minutesAgo: 5), Request("theirs", "other")),
            Team("T2", null, Request("mine-2", Me, TeamAccessRequestStatus.Denied)));

        var mine = await sut.GetMyAccessRequestsAsync();

        Assert.Equal(["mine-2", "mine-1"], mine.Select(x => x.Request.Id));
        Assert.Equal("Team T2", mine[0].TeamName);
    }

    /// <summary>Without teams:read a caller cannot have found a team to request, so the answer is empty rather than an error.</summary>
    [Fact]
    public async Task MyRequests_AreEmpty_WhenTeamsCannotBeListed()
    {
        var (sut, inner) = Build(Team("T1", null, Request("mine", Me)));
        inner.GetAllTeamsAsync().Returns(_ => Throwing());

        Assert.Empty(await sut.GetMyAccessRequestsAsync());

        static async IAsyncEnumerable<ITeam> Throwing()
        {
            await Task.CompletedTask;
            throw new UnauthorizedAccessException();
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    // --- awaiting me ---

    [Fact]
    public async Task AwaitingMe_IsPendingRequestsOnTeamsIManage()
    {
        var (sut, _) = Build(
            Team("managed", AccessLevel.Administrator, Request("pending", "dev"), Request("done", "dev", TeamAccessRequestStatus.Approved)),
            Team("member-only", AccessLevel.User, Request("not-mine-to-decide", "dev")));

        var awaiting = await sut.GetAccessRequestsAwaitingMeAsync();

        var item = Assert.Single(awaiting);
        Assert.Equal("pending", item.Request.Id);
        Assert.Equal("managed", item.TeamKey);
    }

    [Fact]
    public async Task AwaitingMe_LeavesOutMyOwnRequest()
    {
        var (sut, _) = Build(Team("T1", AccessLevel.Owner, Request("mine", Me)));

        Assert.Empty(await sut.GetAccessRequestsAwaitingMeAsync());
    }

    [Fact]
    public void TheFacet_IsRegisteredAndCheckedWithTheOthers()
        => Assert.Contains(typeof(ITeamAccessRequestService), TeamServiceFacets.All);
}
