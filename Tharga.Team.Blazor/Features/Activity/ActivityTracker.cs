using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.WorkTime;

namespace Tharga.Team.Blazor.Features.Activity;

/// <summary>
/// Sums what the browser reports into one write per person per slot (Tharga/Team#399): last seen of the user and of the member in the selected team, and,
/// when the host has turned it on, the active minutes of the slot for the record of when people worked.
/// </summary>
/// <remarks>
/// <b>Buffered per circuit, written when the slot or the team changes and when the circuit closes.</b> The previous throttle slid: it wrote at the first
/// resolve and then not for fifteen minutes, so what it recorded was when someone arrived, and a short burst of work left no trace. Here the value written
/// is the last time the page reported use in the slot, so last seen means <i>last active</i>. Both writes only ever move the value forward, so several tabs
/// and several instances merge into the latest. The work-time write or's the slot's minutes into what is stored, which merges the same way.
/// <para>
/// A write that fails is logged and dropped, each on its own so one cannot stop another: tracking activity must never break a page, and the next slot writes a
/// later time anyway.
/// </para>
/// </remarks>
internal sealed class ActivityTracker(IServiceProvider services, ILogger<ActivityTracker> logger = null) : IActivitySignal, IAsyncDisposable
{
    /// <summary>One write per person per slot, as before.</summary>
    internal static readonly TimeSpan Slot = WorkTimeDay.Slot;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _attachLock = new();
    private long _slot;
    private string _team;
    private DateTime? _bufferedAt;
    private int _minutes;
    private bool _attached;

    public DateTimeOffset? LastActive { get; private set; }

    public event EventHandler Active;

    /// <summary>Claims the page's one watcher. False when another component already holds it, so two never report the same use twice.</summary>
    public bool TryAttach()
    {
        lock (_attachLock)
        {
            if (_attached) return false;

            _attached = true;
            return true;
        }
    }

    public void Detach()
    {
        lock (_attachLock) _attached = false;
    }

    /// <summary>The page reports use.</summary>
    public async Task ReportAsync()
    {
        var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var team = SelectedTeamKey();
        var slot = now.UtcTicks / Slot.Ticks;

        await _gate.WaitAsync();
        try
        {
            // A new slot, or a new team, closes the buffer: it is what the previous one ended on.
            if (_bufferedAt != null && (slot != _slot || !string.Equals(team, _team, StringComparison.Ordinal)))
            {
                await WriteAsync(_team, _bufferedAt.Value, _slot, _minutes);
                _minutes = 0;
            }

            _slot = slot;
            _team = team;
            _bufferedAt = now.UtcDateTime;
            _minutes |= 1 << WorkTimeDay.MinuteOf(now.UtcDateTime);
            LastActive = now;
        }
        finally
        {
            _gate.Release();
        }

        Raise();
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_bufferedAt == null) return;

            var at = _bufferedAt.Value;
            _bufferedAt = null;
            await WriteAsync(_team, at, _slot, _minutes);
            _minutes = 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string SelectedTeamKey()
        => services.GetService<ITeamStateService>() is { } state && state.TryGetSelectedTeam(out var team) ? team?.Key : null;

    private async Task WriteAsync(string teamKey, DateTime at, long slot, int minutes)
    {
        try
        {
            if (services.GetService<IUserService>() is { } users) await users.SetOwnLastSeenAsync(at);
            if (!string.IsNullOrEmpty(teamKey) && services.GetService<ITeamStateService>() is { } state) await state.SetOwnMemberLastSeenAsync(teamKey, at);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not record activity at {At}.", at);
        }

        await WriteWorkTimeAsync(teamKey, slot, minutes);
    }

    // Opt-in, and only for somebody working in a team: the record is per person, team and day.
    private async Task WriteWorkTimeAsync(string teamKey, long slot, int minutes)
    {
        try
        {
            var options = services.GetService<IOptions<ThargaBlazorOptions>>()?.Value.WorkTime;
            if (options is not { Enabled: true } || minutes == 0 || string.IsNullOrEmpty(teamKey)) return;
            if (services.GetService<IWorkTimeStore>() is not { } store) return;
            if (services.GetService<IUserService>() is not { } users) return;

            var user = await users.GetCurrentUserAsync();
            if (string.IsNullOrEmpty(user?.Key)) return;

            var slotStart = new DateTime(slot * Slot.Ticks, DateTimeKind.Utc);
            await store.RecordAsync(user.Key, teamKey, WorkTimeDay.Of(slotStart, options.TimeZone ?? TimeZoneInfo.Utc), slotStart, minutes);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not record work time for the slot {Slot}.", slot);
        }
    }

    private void Raise()
    {
        var handlers = Active;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "An activity handler threw. Only that listener missed the signal.");
            }
        }
    }
}
