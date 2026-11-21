namespace Tharga.Team.WorkTime;

/// <summary>
/// The caller's own work time (Tharga/Team#399, phase 3): when they started, stopped and took breaks, from the minutes the browser's activity signal recorded.
/// </summary>
/// <remarks>
/// <b>Opt-in, and the caller's own.</b> Nothing is recorded until the host turns work time on (<c>ThargaBlazorOptions.WorkTime</c>), and this reads only the
/// caller's days, resolved on the server, so it takes no user key and needs no scope. Reading somebody else's is not offered here.
/// </remarks>
public interface IWorkTimeService
{
    /// <summary>The caller's days in <paramref name="teamKey"/> from <paramref name="from"/> to <paramref name="to"/>, both included, oldest first. Empty when nothing was recorded.</summary>
    Task<IReadOnlyList<WorkTimeSummary>> GetMyDaysAsync(string teamKey, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>What the caller's days in <paramref name="teamKey"/> from <paramref name="from"/> to <paramref name="to"/> add up to.</summary>
    Task<WorkTimeTrend> GetMyTrendAsync(string teamKey, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
