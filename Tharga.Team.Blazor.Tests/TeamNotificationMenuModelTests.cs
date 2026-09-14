using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The notification bell counts what the caller has to decide, and lists that first.
/// </summary>
public class TeamNotificationMenuModelTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static TeamAccessRequestItem Item(string team, string requester, TeamAccessRequestStatus status = TeamAccessRequestStatus.Pending, int minutesAgo = 0)
        => new(team, $"Team {team}", new TeamAccessRequest
        {
            Id = $"{team}-{requester}",
            RequesterKey = requester,
            RequesterName = requester,
            AccessLevel = AccessLevel.User,
            Duration = TimeSpan.FromHours(8),
            RequestedAt = Now.AddMinutes(-minutesAgo),
            Status = status
        });

    [Fact]
    public void TheCount_IsRequestsAwaitingMe_NotMyOwn()
        => Assert.Equal(2, TeamNotificationMenuModel.Count([Item("A", "alice"), Item("B", "bob")]));

    [Fact]
    public void NothingAwaiting_CountsZero()
        => Assert.Equal(0, TeamNotificationMenuModel.Count(null));

    [Fact]
    public void RequestsToDecide_ComeFirst_NewestFirst_ThenMyWaitingRequests()
    {
        var items = TeamNotificationMenuModel.Items(
            awaitingMe: [Item("A", "alice", minutesAgo: 10), Item("B", "bob")],
            mine: [Item("C", "me"), Item("D", "me", TeamAccessRequestStatus.Denied)],
            TextSet.Empty);

        Assert.Equal(["B", "A", "C"], items.Select(x => x.TeamKey));
        Assert.Equal([true, true, false], items.Select(x => x.NeedsDecision));
    }

    [Fact]
    public void ALineToDecide_NamesTheTeamAndTheRequest()
    {
        var item = Assert.Single(TeamNotificationMenuModel.Items([Item("A", "alice")], [], TextSet.Empty));

        Assert.Equal("Team A: alice asks for User access for 8 hours", item.Text);
    }

    [Fact]
    public void MyWaitingRequest_SaysSo()
        => Assert.Equal("Your request for Team C is waiting.", Assert.Single(TeamNotificationMenuModel.Items([], [Item("C", "me")], TextSet.Empty)).Text);
}
