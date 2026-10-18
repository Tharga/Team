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
