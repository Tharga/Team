using Tharga.Team.WorkTime;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// What a day of recorded minutes says about when somebody worked (Tharga/Team#399, phase 3): start, end, breaks, worked time and sessions, as a pure function
/// of what was recorded and of how long an idle stretch has to be to count as a break.
/// </summary>
public class WorkTimeReportTests
{
    private static readonly DateTime Nine = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Thirty = TimeSpan.FromMinutes(30);

    /// <summary>A day with these minutes active, counted from 09:00 UTC.</summary>
    private static WorkDay Day(params int[] minutesFromNine)
    {
        var slots = new Dictionary<DateTime, int>();
        foreach (var offset in minutesFromNine)
        {
            var at = Nine.AddMinutes(offset);
            var slot = WorkTimeDay.SlotStartOf(at);
            slots[slot] = slots.GetValueOrDefault(slot) | (1 << WorkTimeDay.MinuteOf(at));
        }

        var all = minutesFromNine.Select(x => Nine.AddMinutes(x)).ToArray();
        return new WorkDay("u1", "t1", "2026-11-21", all.Min(), all.Max(), slots);
    }

    [Fact]
    public void ADayWithNoActivity_HasNoStartAndNoEnd()
    {
        var summary = WorkTimeReport.Summarize(new WorkDay("u1", "t1", "2026-11-21", Nine, Nine, new Dictionary<DateTime, int>()), Thirty);

        Assert.Null(summary.Start);
        Assert.Null(summary.End);
        Assert.Equal(TimeSpan.Zero, summary.Worked);
        Assert.Empty(summary.Sessions);
        Assert.Empty(summary.Breaks);
    }

    /// <summary>A minute is a minute long: it starts when it starts and ends one minute later.</summary>
    [Fact]
    public void OneActiveMinute_IsOneMinuteOfWork()
    {
        var summary = WorkTimeReport.Summarize(Day(5), Thirty);

        Assert.Equal(Nine.AddMinutes(5), summary.Start);
        Assert.Equal(Nine.AddMinutes(6), summary.End);
        Assert.Equal(TimeSpan.FromMinutes(1), summary.Worked);
        Assert.Equal(TimeSpan.FromMinutes(1), summary.Active);
    }

    /// <summary>Thinking, reading and phone calls are not a break, so a short idle stretch stays inside the session and counts as work.</summary>
    [Fact]
    public void AShortGap_StaysInsideTheSession_AndCountsAsWork()
    {
        var summary = WorkTimeReport.Summarize(Day(0, 10), Thirty);

        var session = Assert.Single(summary.Sessions);
        Assert.Equal(TimeSpan.FromMinutes(11), session.Duration);
        Assert.Equal(TimeSpan.FromMinutes(11), summary.Worked);
        Assert.Equal(TimeSpan.FromMinutes(2), summary.Active);
        Assert.Empty(summary.Breaks);
    }

    /// <summary>The line is the idle time itself: twenty-nine idle minutes is still work, thirty is a break.</summary>
    [Theory]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    public void TheBreakStartsAtExactlyBreakAfterOfIdleTime(int secondMinute, int sessions)
    {
        var summary = WorkTimeReport.Summarize(Day(0, secondMinute), Thirty);

        Assert.Equal(sessions, summary.Sessions.Count);
        Assert.Equal(sessions - 1, summary.Breaks.Count);
    }

    [Fact]
    public void ABreak_IsTheIdleStretchBetweenTwoSessions()
    {
        var summary = WorkTimeReport.Summarize(Day(0, 1, 2, 100, 101), Thirty);

        Assert.Equal(2, summary.Sessions.Count);
        Assert.Equal(new WorkSession(Nine, Nine.AddMinutes(3)), summary.Sessions[0]);
        Assert.Equal(new WorkSession(Nine.AddMinutes(100), Nine.AddMinutes(102)), summary.Sessions[1]);
        var gap = Assert.Single(summary.Breaks);
        Assert.Equal(new WorkBreak(Nine.AddMinutes(3), Nine.AddMinutes(100)), gap);
        Assert.Equal(TimeSpan.FromMinutes(97), summary.BreakTime);
        Assert.Equal(Nine, summary.Start);
        Assert.Equal(Nine.AddMinutes(102), summary.End);
        Assert.Equal(TimeSpan.FromMinutes(5), summary.Worked);
    }

    /// <summary>Slots are a way of storing minutes, not a boundary of work: the last minute of one and the first of the next are one stretch.</summary>
    [Fact]
    public void MinutesEitherSideOfASlotBoundary_AreOneStretch()
    {
        var summary = WorkTimeReport.Summarize(Day(14, 15), Thirty);

        var session = Assert.Single(summary.Sessions);
        Assert.Equal(TimeSpan.FromMinutes(2), session.Duration);
    }

    /// <summary>Nothing derived is stored, so a different rule applies to what was recorded before it.</summary>
    [Fact]
    public void ADifferentBreakAfter_ReadsTheSameDayDifferently()
    {
        var day = Day(0, 20);

        Assert.Single(WorkTimeReport.Summarize(day, Thirty).Sessions);
        Assert.Equal(2, WorkTimeReport.Summarize(day, TimeSpan.FromMinutes(10)).Sessions.Count);
    }

    /// <summary>Consecutive minutes are never a break, however small the threshold, so it cannot be set below a minute.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ABreakAfterBelowAMinute_IsAMinute(int minutes)
    {
        var summary = WorkTimeReport.Summarize(Day(0, 1, 3), TimeSpan.FromMinutes(minutes));

        Assert.Equal(2, summary.Sessions.Count);
    }

    [Fact]
    public void ActiveMinutes_AreInOrder_WhateverOrderTheSlotsWereStoredIn()
    {
        var slots = new Dictionary<DateTime, int> { [Nine.AddMinutes(30)] = 1, [Nine] = 0b101 };
        var day = new WorkDay("u1", "t1", "2026-11-21", Nine, Nine.AddMinutes(30), slots);

        Assert.Equal([Nine, Nine.AddMinutes(2), Nine.AddMinutes(30)], WorkTimeReport.ActiveMinutes(day));
    }

    // --- trends ---

    private static WorkTimeSummary Worked(DateTime start, int minutes)
        => new("d", start, start.AddMinutes(minutes), TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes), [], []);

    [Fact]
    public void ATrend_AddsUpTheDaysThatHadWork()
    {
        var days = new[] { Worked(Nine, 480), Worked(Nine.AddDays(1).AddHours(1), 360), new WorkTimeSummary("empty", null, null, TimeSpan.Zero, TimeSpan.Zero, [], []) };

        var trend = WorkTimeReport.Trend(days, TimeZoneInfo.Utc);

        Assert.Equal(2, trend.Days);
        Assert.Equal(TimeSpan.FromMinutes(840), trend.TotalWorked);
        Assert.Equal(TimeSpan.FromMinutes(420), trend.AverageWorked);
        Assert.Equal(TimeSpan.FromHours(9.5), trend.AverageStart);
        Assert.Equal(TimeSpan.FromHours(9.5) + TimeSpan.FromMinutes(420), trend.AverageEnd);
    }

    [Fact]
    public void ATrendOfNoWork_IsNothing()
    {
        var trend = WorkTimeReport.Trend([], TimeZoneInfo.Utc);

        Assert.Equal(0, trend.Days);
        Assert.Null(trend.AverageStart);
        Assert.Null(trend.AverageEnd);
    }

    /// <summary>The time of day people start is the time on their clock, not on the server's.</summary>
    [Fact]
    public void ATrendsTimesOfDay_AreInTheGivenZone()
    {
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");

        var trend = WorkTimeReport.Trend([Worked(Nine, 60)], plusTwo);

        Assert.Equal(TimeSpan.FromHours(11), trend.AverageStart);
    }
}

/// <summary>Which day a moment belongs to and which slot and minute it is in (Tharga/Team#399, phase 3).</summary>
public class WorkTimeDayTests
{
    private static readonly TimeZoneInfo PlusOne = TimeZoneInfo.CreateCustomTimeZone("plus1", TimeSpan.FromHours(1), "plus1", "plus1");
    private static readonly TimeZoneInfo PlusFiveThirtyFive = TimeZoneInfo.CreateCustomTimeZone("nepal", new TimeSpan(5, 45, 0), "nepal", "nepal");

    [Fact]
    public void TheDay_IsCutWhereTheZoneCutsIt()
    {
        var lateEvening = new DateTime(2026, 11, 21, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal("2026-11-21", WorkTimeDay.Of(lateEvening, TimeZoneInfo.Utc));
        Assert.Equal("2026-11-22", WorkTimeDay.Of(lateEvening, PlusOne));
    }

    /// <summary>Every offset in use is a multiple of fifteen minutes, so a slot is never split between two days. Nepal, at a quarter to the hour, is the case that shows it.</summary>
    [Fact]
    public void ASlot_NeverStraddlesTwoDays()
    {
        var slotBeforeMidnight = new DateTime(2026, 11, 21, 18, 0, 0, DateTimeKind.Utc);

        Assert.Equal("2026-11-21", WorkTimeDay.Of(slotBeforeMidnight, PlusFiveThirtyFive));
        Assert.Equal("2026-11-21", WorkTimeDay.Of(slotBeforeMidnight.AddMinutes(14), PlusFiveThirtyFive));
        Assert.Equal("2026-11-22", WorkTimeDay.Of(slotBeforeMidnight.AddMinutes(15), PlusFiveThirtyFive));
    }

    [Fact]
    public void ASlot_StartsOnTheQuarterHour_AndAMinuteIsItsPlaceInIt()
    {
        var moment = new DateTime(2026, 11, 21, 9, 37, 42, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 21, 9, 30, 0, DateTimeKind.Utc), WorkTimeDay.SlotStartOf(moment));
        Assert.Equal(7, WorkTimeDay.MinuteOf(moment));
        Assert.Equal(DateTimeKind.Utc, WorkTimeDay.SlotStartOf(moment).Kind);
    }

    [Fact]
    public void ADayIsTextThatSortsAsADateDoes_AndParsesBack()
    {
        var day = new DateOnly(2026, 1, 9);

        Assert.Equal("2026-01-09", WorkTimeDay.Text(day));
        Assert.Equal(day, WorkTimeDay.Parse("2026-01-09"));
        Assert.Null(WorkTimeDay.Parse("9 Jan"));
        Assert.True(string.CompareOrdinal(WorkTimeDay.Text(new DateOnly(2026, 2, 1)), WorkTimeDay.Text(new DateOnly(2026, 1, 31))) > 0);
    }

    [Fact]
    public void TheMinutesOfAMask_AreTheStartsOfTheBitsSet()
    {
        var slot = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal([slot, slot.AddMinutes(3), slot.AddMinutes(14)], WorkTimeDay.MinutesIn(slot, (1 << 0) | (1 << 3) | (1 << 14)));
        Assert.Empty(WorkTimeDay.MinutesIn(slot, 0));
    }
}
