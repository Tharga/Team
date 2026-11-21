using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tharga.Team.Blazor.Features.Activity;
using Tharga.Team.Blazor.Features.Team;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What the browser reports is summed into one write per person per slot, and what is written is when they were last active, not when they arrived
/// (Tharga/Team#399).
/// </summary>
public class ActivityTrackerTests
{
    private static readonly DateTimeOffset SlotStart = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly ManualTime _time = new(SlotStart);
    private readonly Mock<IUserService> _users = new();
    private readonly Mock<ITeamStateService> _teams = new();
    private string _selected = "team-1";

    private ActivityTracker Sut()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_users.Object);
        services.AddSingleton<ITeamStateService>(new FakeState(() => _selected, _teams.Object));
        return new ActivityTracker(services.BuildServiceProvider());
    }

    [Fact]
    public async Task Activity_WithinOneSlot_IsNotWrittenUntilTheSlotEnds()
    {
        var sut = Sut();

        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        await sut.ReportAsync();

        _users.Verify(x => x.SetOwnLastSeenAsync(It.IsAny<DateTime>()), Times.Never);
        _teams.Verify(x => x.SetOwnMemberLastSeenAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    /// <summary>The value is the last time in the slot, so a short burst leaves a trace of when it ended, which the sliding throttle never did.</summary>
    [Fact]
    public async Task WhenTheSlotChanges_TheLastActiveTimeOfTheSlotIsWritten_ForUserAndMember()
    {
        var sut = Sut();
        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromMinutes(7));
        await sut.ReportAsync();
        var lastInSlot = _time.GetUtcNow().UtcDateTime;

        _time.Advance(TimeSpan.FromMinutes(10));
        await sut.ReportAsync();

        _users.Verify(x => x.SetOwnLastSeenAsync(lastInSlot), Times.Once);
        _teams.Verify(x => x.SetOwnMemberLastSeenAsync("team-1", lastInSlot), Times.Once);
    }

    [Fact]
    public async Task WhenTheCircuitCloses_WhatWasBufferedIsWritten()
    {
        var sut = Sut();
        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromMinutes(3));
        await sut.ReportAsync();
        var last = _time.GetUtcNow().UtcDateTime;

        await sut.DisposeAsync();

        _users.Verify(x => x.SetOwnLastSeenAsync(last), Times.Once);
        _teams.Verify(x => x.SetOwnMemberLastSeenAsync("team-1", last), Times.Once);
    }

    [Fact]
    public async Task ClosingTwice_WritesOnce()
    {
        var sut = Sut();
        await sut.ReportAsync();

        await sut.DisposeAsync();
        await sut.DisposeAsync();

        _users.Verify(x => x.SetOwnLastSeenAsync(It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task ClosingWithNothingReported_WritesNothing()
    {
        var sut = Sut();

        await sut.DisposeAsync();

        _users.Verify(x => x.SetOwnLastSeenAsync(It.IsAny<DateTime>()), Times.Never);
    }

    /// <summary>Each team's member has their own last seen, so moving to another team closes what was buffered for the first.</summary>
    [Fact]
    public async Task SwitchingTeam_WritesWhatWasBufferedForTheFirst()
    {
        var sut = Sut();
        await sut.ReportAsync();
        var first = _time.GetUtcNow().UtcDateTime;

        _time.Advance(TimeSpan.FromMinutes(1));
        _selected = "team-2";
        await sut.ReportAsync();

        _teams.Verify(x => x.SetOwnMemberLastSeenAsync("team-1", first), Times.Once);
        _teams.Verify(x => x.SetOwnMemberLastSeenAsync("team-2", It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task WithNoTeamSelected_OnlyTheUserIsStamped()
    {
        _selected = null;
        var sut = Sut();
        await sut.ReportAsync();

        await sut.DisposeAsync();

        _users.Verify(x => x.SetOwnLastSeenAsync(It.IsAny<DateTime>()), Times.Once);
        _teams.Verify(x => x.SetOwnMemberLastSeenAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    /// <summary>Activity tracking must never break a page, so a failed write is dropped; the next slot writes a later time anyway.</summary>
    [Fact]
    public async Task AFailedWrite_DoesNotBreakTheReport()
    {
        _users.Setup(x => x.SetOwnLastSeenAsync(It.IsAny<DateTime>())).ThrowsAsync(new InvalidOperationException("store down"));
        var sut = Sut();
        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromMinutes(16));

        await sut.ReportAsync();

        Assert.Equal(_time.GetUtcNow(), sut.LastActive);
    }

    [Fact]
    public async Task EveryReport_RaisesTheSignal_AndRecordsWhen()
    {
        var sut = Sut();
        var raised = 0;
        sut.Active += (_, _) => raised++;

        await sut.ReportAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        await sut.ReportAsync();

        Assert.Equal(2, raised);
        Assert.Equal(_time.GetUtcNow(), sut.LastActive);
    }

    [Fact]
    public async Task AListenerThatThrows_CostsOnlyItsOwnNotice()
    {
        var sut = Sut();
        var heard = false;
        sut.Active += (_, _) => throw new InvalidOperationException("bad listener");
        sut.Active += (_, _) => heard = true;

        await sut.ReportAsync();

        Assert.True(heard);
    }

    [Fact]
    public void OnlyOneWatcherAtATime_HoldsThePage()
    {
        var sut = Sut();

        Assert.True(sut.TryAttach());
        Assert.False(sut.TryAttach());

        sut.Detach();

        Assert.True(sut.TryAttach());
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
