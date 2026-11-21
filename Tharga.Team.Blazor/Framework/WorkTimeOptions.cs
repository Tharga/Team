using Tharga.Team.WorkTime;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Settings for the opt-in record of when people worked (Tharga/Team#399). <b>Off by default</b>: a record of when somebody started, stopped and took breaks is
/// personal data, so nothing is kept until the host decides to and has told its people.
/// </summary>
public sealed class WorkTimeOptions
{
    /// <summary>
    /// Whether the active minutes the browser reports are recorded, one record per person, team and day. Default <c>false</c>. Needs an
    /// <see cref="IWorkTimeStore"/>, which <c>AddThargaTeamRepository</c> registers; with none, nothing is recorded.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The time zone that cuts one day from the next. Default UTC. Set it to where the people work, or a day's work is split at the wrong hour. Changing it
    /// does not move what is already recorded: those days keep the cut they were written with.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// How long somebody has to be idle for it to count as a break rather than part of the work. Default 30 minutes, never less than one. It is applied when a
    /// day is read, not when it is recorded, so changing it applies to history too.
    /// </summary>
    public TimeSpan BreakAfter { get; set; } = WorkTimeReport.DefaultBreakAfter;
}
