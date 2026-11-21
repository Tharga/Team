namespace Tharga.Team.WorkTime;

/// <summary>A stretch of work: from its first active minute to the end of its last, with nothing idle for <c>BreakAfter</c> or longer inside it.</summary>
/// <param name="Start">When it began, in UTC.</param>
/// <param name="End">When it ended, in UTC: the end of the last active minute.</param>
public sealed record WorkSession(DateTime Start, DateTime End)
{
    /// <summary>How long the session lasted, idle stretches shorter than a break included.</summary>
    public TimeSpan Duration => End - Start;
}

/// <summary>An idle stretch of <c>BreakAfter</c> or longer between two sessions.</summary>
/// <param name="Start">When the previous session ended, in UTC.</param>
/// <param name="End">When the next session began, in UTC.</param>
public sealed record WorkBreak(DateTime Start, DateTime End)
{
    /// <summary>How long the break lasted.</summary>
    public TimeSpan Duration => End - Start;
}

/// <summary>One day's work, derived from the recorded minutes.</summary>
/// <param name="Day">The day, as <c>yyyy-MM-dd</c>.</param>
/// <param name="Start">When work began, in UTC, or <c>null</c> for a day with no activity.</param>
/// <param name="End">When work ended, in UTC, or <c>null</c> for a day with no activity.</param>
/// <param name="Worked">The sum of the sessions: time at work, short idle stretches counted as work.</param>
/// <param name="Active">The minutes in which the person actually did something.</param>
/// <param name="Sessions">The stretches of work, earliest first.</param>
/// <param name="Breaks">The breaks between them, earliest first.</param>
public sealed record WorkTimeSummary(string Day, DateTime? Start, DateTime? End, TimeSpan Worked, TimeSpan Active, IReadOnlyList<WorkSession> Sessions, IReadOnlyList<WorkBreak> Breaks)
{
    /// <summary>The sum of the breaks.</summary>
    public TimeSpan BreakTime => TimeSpan.FromTicks(Breaks.Sum(x => x.Duration.Ticks));
}

/// <summary>What a range of days adds up to.</summary>
/// <param name="Days">How many of the days had any work.</param>
/// <param name="TotalWorked">The sum of the days' worked time.</param>
/// <param name="AverageWorked">The average worked time of a day that had work, or zero for none.</param>
/// <param name="AverageStart">The average local time of day work began, or <c>null</c> for no work.</param>
/// <param name="AverageEnd">The average local time of day work ended, or <c>null</c> for no work.</param>
public sealed record WorkTimeTrend(int Days, TimeSpan TotalWorked, TimeSpan AverageWorked, TimeSpan? AverageStart, TimeSpan? AverageEnd);

/// <summary>
/// What a day of recorded minutes says about when somebody worked (Tharga/Team#399, phase 3). Pure: nothing derived is stored, so a different
/// <c>BreakAfter</c>, or a better rule, applies to history too.
/// </summary>
/// <remarks>
/// <b>A minute is active, an idle stretch is a gap between active minutes.</b> A gap of <c>breakAfter</c> or longer is a break and ends a session; a shorter
/// one is thinking, reading or a phone call and stays inside it. Worked time is the sessions, so it is longer than the active minutes by exactly those short gaps.
/// <para>
/// A day is cut where the configured time zone cuts it, so work either side of midnight is two days, and a session never spans two.
/// </para>
/// </remarks>
public static class WorkTimeReport
{
    /// <summary>How long an idle stretch has to be to count as a break, unless the host says otherwise.</summary>
    public static readonly TimeSpan DefaultBreakAfter = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>Every active minute of <paramref name="day"/>, as the start of the minute, earliest first.</summary>
    public static IReadOnlyList<DateTime> ActiveMinutes(WorkDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        return [.. day.Slots.OrderBy(x => x.Key).SelectMany(x => WorkTimeDay.MinutesIn(x.Key, x.Value))];
    }

    /// <summary>Start, end, sessions, breaks and worked time of <paramref name="day"/>.</summary>
    /// <param name="day">The recorded day.</param>
    /// <param name="breakAfter">How long an idle stretch has to be to count as a break. Never less than a minute.</param>
    public static WorkTimeSummary Summarize(WorkDay day, TimeSpan breakAfter)
    {
        var minutes = ActiveMinutes(day);
        if (minutes.Count == 0) return new WorkTimeSummary(day.Day, null, null, TimeSpan.Zero, TimeSpan.Zero, [], []);

        var threshold = breakAfter < Minute ? Minute : breakAfter;
        var sessions = new List<WorkSession>();
        var breaks = new List<WorkBreak>();

        var start = minutes[0];
        var previous = minutes[0];
        foreach (var minute in minutes.Skip(1))
        {
            var idle = minute - (previous + Minute);
            if (idle >= threshold)
            {
                sessions.Add(new WorkSession(start, previous + Minute));
                breaks.Add(new WorkBreak(previous + Minute, minute));
                start = minute;
            }

            previous = minute;
        }

        sessions.Add(new WorkSession(start, previous + Minute));

        return new WorkTimeSummary(
            day.Day,
            sessions[0].Start,
            sessions[^1].End,
            TimeSpan.FromTicks(sessions.Sum(x => x.Duration.Ticks)),
            TimeSpan.FromMinutes(minutes.Count),
            sessions,
            breaks);
    }

    /// <summary>What <paramref name="days"/> add up to, with start and end as the local time of day in <paramref name="zone"/>.</summary>
    public static WorkTimeTrend Trend(IEnumerable<WorkTimeSummary> days, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(zone);

        var worked = days.Where(x => x.Start != null && x.End != null).ToList();
        if (worked.Count == 0) return new WorkTimeTrend(0, TimeSpan.Zero, TimeSpan.Zero, null, null);

        var total = TimeSpan.FromTicks(worked.Sum(x => x.Worked.Ticks));

        return new WorkTimeTrend(
            worked.Count,
            total,
            TimeSpan.FromTicks(total.Ticks / worked.Count),
            TimeSpan.FromTicks((long)worked.Average(x => TimeOfDay(x.Start!.Value, zone).Ticks)),
            TimeSpan.FromTicks((long)worked.Average(x => TimeOfDay(x.End!.Value, zone).Ticks)));
    }

    private static TimeSpan TimeOfDay(DateTime utc, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).TimeOfDay;
}
