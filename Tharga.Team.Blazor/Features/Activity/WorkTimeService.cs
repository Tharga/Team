using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.WorkTime;

namespace Tharga.Team.Blazor.Features.Activity;

/// <summary>
/// The caller's own work time (Tharga/Team#399, phase 3), read from the store and summarised by <see cref="WorkTimeReport"/> with the host's
/// <see cref="WorkTimeOptions.BreakAfter"/>.
/// </summary>
/// <remarks>
/// <b>Only the caller's days.</b> The user is resolved here, on the server, so nothing takes a user key and there is nothing to authorise: it is the same
/// person who produced the record. A store is read, not a team, so being a member is not asked for; a person who has left a team can still see their own days
/// in it.
/// </remarks>
internal sealed class WorkTimeService(IServiceProvider services, IOptions<ThargaBlazorOptions> options) : IWorkTimeService
{
    // Resolved when asked, not injected, so a host that keeps no work time, or has no user service, still starts; asking then says what is missing.
    private IWorkTimeStore store => services.GetService<IWorkTimeStore>() ?? throw new InvalidOperationException($"No {nameof(IWorkTimeStore)} is registered to read work time from. Call AddThargaTeamRepository (Tharga.Team.MongoDB), which registers one, or register your own.");

    public async Task<IReadOnlyList<WorkTimeSummary>> GetMyDaysAsync(string teamKey, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(teamKey) || to < from) return [];

        var user = await (services.GetService<IUserService>() ?? throw new InvalidOperationException($"No {nameof(IUserService)} is registered.")).GetCurrentUserAsync();
        if (string.IsNullOrEmpty(user?.Key)) return [];

        var breakAfter = options.Value.WorkTime.BreakAfter;
        var summaries = new List<WorkTimeSummary>();
        await foreach (var day in store.GetAsync(user.Key, teamKey, WorkTimeDay.Text(from), WorkTimeDay.Text(to), cancellationToken))
        {
            summaries.Add(WorkTimeReport.Summarize(day, breakAfter));
        }

        return summaries;
    }

    public async Task<WorkTimeTrend> GetMyTrendAsync(string teamKey, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => WorkTimeReport.Trend(await GetMyDaysAsync(teamKey, from, to, cancellationToken), options.Value.WorkTime.TimeZone ?? TimeZoneInfo.Utc);
}
