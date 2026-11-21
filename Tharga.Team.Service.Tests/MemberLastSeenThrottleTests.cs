using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The member's Last Seen is stamped on activity in the team (Tharga/Team#317), so it is throttled per team and
/// user, like the user's own stamp, or every page reading the selected team would write to the store.
/// </summary>
/// <remarks>The throttle is per process, so each test uses a team key of its own.</remarks>
public class MemberLastSeenThrottleTests
{
    private static (TestTeamService Sut, string TeamKey) Build(TimeSpan? interval, string userKey = "u1")
    {
        var userService = Substitute.For<IUserService>();
        if (userKey != null)
        {
            var user = Substitute.For<IUser>();
            user.Key.Returns(userKey);
            userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);
        }
        else
        {
            userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns((IUser)null);
        }

        return (new TestTeamService(userService) { StampInterval = interval }, $"team-{Guid.NewGuid():N}");
    }

    [Fact]
    public async Task TheFirstCall_Stamps()
    {
        var (sut, team) = Build(TimeSpan.FromMinutes(15));

        await sut.SetMemberLastSeenAsync(team);

        Assert.Equal([(team, "u1")], sut.LastSeenStamps);
    }

    [Fact]
    public async Task ACallWithinTheInterval_IsThrottled()
    {
        var (sut, team) = Build(TimeSpan.FromMinutes(15));

        await sut.SetMemberLastSeenAsync(team);
        await sut.SetMemberLastSeenAsync(team);

        Assert.Single(sut.LastSeenStamps);
    }

    /// <summary>Per process, not per instance: the service is scoped, so a new request gets a new one.</summary>
    [Fact]
    public async Task TheThrottle_OutlivesTheServiceInstance()
    {
        var (first, team) = Build(TimeSpan.FromMinutes(15));
        await first.SetMemberLastSeenAsync(team);

        var (second, _) = Build(TimeSpan.FromMinutes(15));
        await second.SetMemberLastSeenAsync(team);

        Assert.Empty(second.LastSeenStamps);
    }

    [Fact]
    public async Task EachTeam_IsThrottledOnItsOwn()
    {
        var (sut, team) = Build(TimeSpan.FromMinutes(15));
        var otherTeam = $"{team}-other";

        await sut.SetMemberLastSeenAsync(team);
        await sut.SetMemberLastSeenAsync(otherTeam);

        Assert.Equal([(team, "u1"), (otherTeam, "u1")], sut.LastSeenStamps);
    }

    [Fact]
    public async Task EachMember_IsThrottledOnTheirOwn()
    {
        var (first, team) = Build(TimeSpan.FromMinutes(15), userKey: "u1");
        await first.SetMemberLastSeenAsync(team);

        var (second, _) = Build(TimeSpan.FromMinutes(15), userKey: "u2");
        await second.SetMemberLastSeenAsync(team);

        Assert.Equal([(team, "u2")], second.LastSeenStamps);
    }

    [Fact]
    public async Task AZeroInterval_StampsEveryTime()
    {
        var (sut, team) = Build(TimeSpan.Zero);

        await sut.SetMemberLastSeenAsync(team);
        await sut.SetMemberLastSeenAsync(team);

        Assert.Equal(2, sut.LastSeenStamps.Count);
    }

    [Fact]
    public async Task ANullInterval_TurnsStampingOff()
    {
        var (sut, team) = Build(interval: null);

        await sut.SetMemberLastSeenAsync(team);

        Assert.Empty(sut.LastSeenStamps);
    }

    [Fact]
    public async Task WithNoCurrentUser_NothingIsStamped()
    {
        var (sut, team) = Build(TimeSpan.Zero, userKey: null);

        await sut.SetMemberLastSeenAsync(team);

        Assert.Empty(sut.LastSeenStamps);
    }
}

/// <summary>
/// The browser's activity signal names when a member was last active, and the write is not throttled, since the signal has already summed a slot into one call
/// (Tharga/Team#399).
/// </summary>
public class MemberLastActiveTests
{
    private static (TestTeamService Sut, string TeamKey) Build(TimeSpan? interval = null, string userKey = "u1")
    {
        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns(userKey);
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);

        return (new TestTeamService(userService) { StampInterval = interval ?? TimeSpan.FromMinutes(15) }, $"team-{Guid.NewGuid():N}");
    }

    [Fact]
    public async Task TheTimeTheSignalNames_IsWhatIsWritten_ForTheCaller()
    {
        var (sut, team) = Build();
        var lastActive = new DateTime(2026, 11, 21, 9, 7, 0, DateTimeKind.Utc);

        await sut.SetOwnMemberLastSeenAsync(team, lastActive);

        Assert.Equal([(team, "u1", lastActive)], sut.LastSeenAtStamps);
    }

    [Fact]
    public async Task EveryCall_IsWritten_NotThrottled()
    {
        var (sut, team) = Build();

        await sut.SetOwnMemberLastSeenAsync(team, new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc));
        await sut.SetOwnMemberLastSeenAsync(team, new DateTime(2026, 11, 21, 9, 15, 0, DateTimeKind.Utc));

        Assert.Equal(2, sut.LastSeenAtStamps.Count);
    }

    [Fact]
    public async Task WithStampingTurnedOff_NothingIsWritten()
    {
        var userService = Substitute.For<IUserService>();
        var sut = new TestTeamService(userService) { StampInterval = null };

        await sut.SetOwnMemberLastSeenAsync("team-x", DateTime.UtcNow);

        Assert.Empty(sut.LastSeenAtStamps);
    }

    [Fact]
    public async Task WithNoTeam_NothingIsWritten()
    {
        var (sut, _) = Build();

        await sut.SetOwnMemberLastSeenAsync(null, DateTime.UtcNow);

        Assert.Empty(sut.LastSeenAtStamps);
    }

    /// <summary>A store that has not taken the time still records the activity, a little late, through the method it already implements.</summary>
    [Fact]
    public async Task AStoreThatHasNotTakenTheTime_StillRecordsTheActivity()
    {
        var userService = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("u1");
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(user);
        var sut = new TestTeamServiceWithoutTime(userService);

        await sut.SetOwnMemberLastSeenAsync("team-y", DateTime.UtcNow);

        Assert.Equal([("team-y", "u1")], sut.LastSeenStamps);
    }

    private sealed class TestTeamServiceWithoutTime(IUserService userService) : TestTeamService(userService)
    {
        protected override Task SetTeamMemberLastSeenAsync(string teamKey, string userKey, DateTime lastSeen)
            => SetTeamMemberLastSeenAsync(teamKey, userKey);
    }
}
