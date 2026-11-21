using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team.Blazor.Features.Activity;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.WorkTime;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The opt-in record of when people worked (Tharga/Team#399, phase 3): off unless the host turns it on, the active minutes of a slot written once, per person,
/// team and day, and the caller's own days read back as start, end and breaks.
/// </summary>
public class WorkTimeTests
{
    private static readonly DateTimeOffset SlotStart = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly ManualTime _time = new(SlotStart);
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<IWorkTimeStore> _store = new();
    private readonly Mock<ITeamStateService> _teams = new();
    private string _selected = "team-1";

    public WorkTimeTests()
    {
        var user = new Mock<IUser>();
        user.SetupGet(x => x.Key).Returns("u1");
        _users.Setup(x => x.GetCurrentUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user.Object);
    }

    private ServiceCollection Services(WorkTimeOptions workTime, bool withStore = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_users.Object);
        services.AddSingleton<ITeamStateService>(new FakeState(() => _selected, _teams.Object));
        services.AddSingleton(Options.Create(new ThargaBlazorOptions { WorkTime = workTime }));
        if (withStore) services.AddSingleton(_store.Object);
        return services;
    }

    private ActivityTracker Tracker(WorkTimeOptions workTime, bool withStore = true)
        => new(Services(workTime, withStore).BuildServiceProvider());

    private static WorkTimeOptions On(TimeZoneInfo zone = null) => new() { Enabled = true, TimeZone = zone ?? TimeZoneInfo.Utc };

    // --- recording ---

    /// <summary>A record of when somebody started and stopped is personal data, so nothing is kept until the host says so.</summary>
    [Fact]
    public async Task ByDefault_NothingIsRecorded()
    {
        var sut = Tracker(new WorkTimeOptions());
        await sut.ReportAsync();

        await sut.DisposeAsync();

        _store.Verify(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenTheCircuitCloses_TheMinutesOfTheSlotAreRecorded_ForThePersonTeamAndDay()
    {
        var sut = Tracker(On());
        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromMinutes(3));
        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromSeconds(20));
        await sut.ReportAsync();

        await sut.DisposeAsync();

        _store.Verify(x => x.RecordAsync("u1", "team-1", "2026-11-21", SlotStart.UtcDateTime, (1 << 0) | (1 << 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Written once per slot, when the slot is over, and what was written is only that slot's minutes.</summary>
    [Fact]
    public async Task WhenTheSlotChanges_ThatSlotIsRecorded_AndTheNextStartsEmpty()
    {
        var sut = Tracker(On());
        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromMinutes(2));
        await sut.ReportAsync();

        _time.Advance(TimeSpan.FromMinutes(14));
        await sut.ReportAsync();
        await sut.DisposeAsync();

        _store.Verify(x => x.RecordAsync("u1", "team-1", "2026-11-21", SlotStart.UtcDateTime, (1 << 0) | (1 << 2), It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(x => x.RecordAsync("u1", "team-1", "2026-11-21", SlotStart.UtcDateTime.AddMinutes(15), 1 << 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithinOneSlot_NothingIsRecordedYet()
    {
        var sut = Tracker(On());

        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await sut.ReportAsync();

        _store.Verify(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The record is per team, so a person in none has nothing to record it against.</summary>
    [Fact]
    public async Task WithNoTeamSelected_NothingIsRecorded()
    {
        _selected = null;
        var sut = Tracker(On());
        await sut.ReportAsync();

        await sut.DisposeAsync();

        _store.Verify(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SwitchingTeam_RecordsWhatWasBufferedForTheFirst()
    {
        var sut = Tracker(On());
        await sut.ReportAsync();

        _time.Advance(TimeSpan.FromMinutes(1));
        _selected = "team-2";
        await sut.ReportAsync();

        _store.Verify(x => x.RecordAsync("u1", "team-1", "2026-11-21", SlotStart.UtcDateTime, 1 << 0, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(x => x.RecordAsync("u1", "team-2", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The day is cut where the host's time zone cuts it: half past eleven at night in UTC is already tomorrow one hour east.</summary>
    [Fact]
    public async Task TheDay_IsTheOneInTheConfiguredZone()
    {
        var plusOne = TimeZoneInfo.CreateCustomTimeZone("plus1", TimeSpan.FromHours(1), "plus1", "plus1");
        var late = new ManualTime(new DateTimeOffset(2026, 11, 21, 23, 30, 0, TimeSpan.Zero));
        var services = Services(On(plusOne));
        services.AddSingleton<TimeProvider>(late);
        var sut = new ActivityTracker(services.BuildServiceProvider());
        await sut.ReportAsync();

        await sut.DisposeAsync();

        _store.Verify(x => x.RecordAsync("u1", "team-1", "2026-11-22", It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithNoStoreRegistered_NothingBreaks()
    {
        var sut = Tracker(On(), withStore: false);
        await sut.ReportAsync();

        await sut.DisposeAsync();

        Assert.NotNull(sut.LastActive);
    }

    /// <summary>The two writes are independent: a store that is down must not cost the person their last seen.</summary>
    [Fact]
    public async Task AFailedWorkTimeWrite_DoesNotStopLastSeen()
    {
        _store.Setup(x => x.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store down"));
        var sut = Tracker(On());
        await sut.ReportAsync();

        await sut.DisposeAsync();

        _users.Verify(x => x.SetOwnLastSeenAsync(It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task AFailedLastSeenWrite_DoesNotStopWorkTime()
    {
        _users.Setup(x => x.SetOwnLastSeenAsync(It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("store down"));
        var sut = Tracker(On());
        await sut.ReportAsync();

        await sut.DisposeAsync();

        _store.Verify(x => x.RecordAsync("u1", "team-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- reading ---

    private static async IAsyncEnumerable<WorkDay> Days(params WorkDay[] days)
    {
        foreach (var day in days)
        {
            await Task.Yield();
            yield return day;
        }
    }

    private static WorkDay Worked(string day, DateTime start, params int[] minutes)
        => new("u1", "team-1", day, start, start.AddMinutes(minutes.Max()), new Dictionary<DateTime, int> { [start] = minutes.Aggregate(0, (mask, minute) => mask | (1 << minute)) });

    private WorkTimeService Reader(WorkTimeOptions workTime, bool withStore = true)
        => new(Services(workTime, withStore).BuildServiceProvider(), Options.Create(new ThargaBlazorOptions { WorkTime = workTime }));

    [Fact]
    public async Task TheCallersDays_AreSummarised_WithTheHostsBreakAfter()
    {
        var nine = SlotStart.UtcDateTime;
        _store.Setup(x => x.GetAsync("u1", "team-1", "2026-11-21", "2026-11-22", It.IsAny<CancellationToken>()))
            .Returns(Days(Worked("2026-11-21", nine, 0, 1, 12)));

        var summaries = await Reader(new WorkTimeOptions { BreakAfter = TimeSpan.FromMinutes(5) }).GetMyDaysAsync("team-1", new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 22));

        var day = Assert.Single(summaries);
        Assert.Equal(2, day.Sessions.Count);
        Assert.Single(day.Breaks);
    }

    /// <summary>The caller is resolved on the server, so the store is only ever asked for the caller's own key.</summary>
    [Fact]
    public async Task OnlyTheCallersOwnKey_IsEverRead()
    {
        _store.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Days());

        await Reader(On()).GetMyDaysAsync("team-1", new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 21));

        _store.Verify(x => x.GetAsync("u1", "team-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(x => x.GetAsync(It.Is<string>(k => k != "u1"), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutATeam_ThereAreNoDays(string team)
    {
        Assert.Empty(await Reader(On()).GetMyDaysAsync(team, new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 22)));
    }

    [Fact]
    public async Task ABackwardsRange_IsEmpty()
    {
        Assert.Empty(await Reader(On()).GetMyDaysAsync("team-1", new DateOnly(2026, 11, 22), new DateOnly(2026, 11, 21)));
    }

    [Fact]
    public async Task WithNoStoreRegistered_AskingSaysWhatIsMissing()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Reader(On(), withStore: false).GetMyDaysAsync("team-1", new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 21)));

        Assert.Contains(nameof(IWorkTimeStore), error.Message);
    }

    [Fact]
    public async Task TheTrend_IsInTheHostsZone()
    {
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
        _store.Setup(x => x.GetAsync("u1", "team-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Days(Worked("2026-11-21", SlotStart.UtcDateTime, 0)));

        var trend = await Reader(On(plusTwo)).GetMyTrendAsync("team-1", new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 21));

        Assert.Equal(1, trend.Days);
        Assert.Equal(TimeSpan.FromHours(11), trend.AverageStart);
    }

    // --- options ---

    [Fact]
    public void WorkTime_IsOffByDefault_InUtc_WithHalfAnHourForABreak()
    {
        var options = new ThargaBlazorOptions().WorkTime;

        Assert.False(options.Enabled);
        Assert.Equal(TimeZoneInfo.Utc, options.TimeZone);
        Assert.Equal(TimeSpan.FromMinutes(30), options.BreakAfter);
    }

    private sealed class FakeState(Func<string> key, ITeamStateService writes) : ITeamStateService
    {
        public bool TryGetSelectedTeam(out ITeam team)
        {
            var current = key();
            if (current == null)
            {
                team = null;
                return false;
            }

            var mock = new Mock<ITeam>();
            mock.SetupGet(x => x.Key).Returns(current);
            team = mock.Object;
            return true;
        }

        public event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent { add { } remove { } }
        public event EventHandler<SelectedTeamChangedEventArgs> SelectedTeamChangedEvent { add { } remove { } }
        public Task<ITeam> GetSelectedTeamAsync() => throw new NotSupportedException();
        public Task SetSelectedTeamAsync(ITeam selectedTeam) => throw new NotSupportedException();
        public Task SetOwnMemberLastSeenAsync(string teamKey, DateTime lastActive) => writes.SetOwnMemberLastSeenAsync(teamKey, lastActive);
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
