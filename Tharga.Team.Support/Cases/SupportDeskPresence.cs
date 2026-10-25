using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Who is on the support desk: each support person's status, and when they last did something in support.
/// </summary>
/// <remarks>
/// <b>Online</b> while somebody chose <see cref="SupportStaffStatus.Online"/> and acted within
/// <see cref="SupportCaseOptions.StaffIdleAfter"/>. <b>Away</b> while somebody chose Online or Away and nobody is online.
/// <b>Unknown</b> otherwise, which renders as nothing: those who chose Hidden are not counted at all, so members are told
/// nothing about support while they answer. Somebody known stays known until the application restarts, so after hours
/// members are told support will reply later.
/// <para>
/// In-process for the same reason as <see cref="SupportTypingNotifier"/>, and with the same consequence: on several
/// instances, each knows only the support people connected to it. Presence is advisory, so a wrong answer costs a
/// member a notice, never a question refused.
/// </para>
/// </remarks>
internal sealed class SupportDeskPresence(IOptions<SupportCaseOptions> options, TimeProvider timeProvider, ILogger<SupportDeskPresence> logger = null) : IDisposable
{
    private readonly TimeSpan _idleAfter = options.Value.StaffIdleAfter;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (SupportStaffStatus Status, DateTimeOffset ActedAt)> _staff = new(StringComparer.Ordinal);

    private SupportPresenceState _state = SupportPresenceState.Unknown;
    private ITimer _timer;

    /// <summary>Raised when somebody sets a status, and when the desk turns online, away or unknown.</summary>
    public event EventHandler Changed;

    /// <summary>Whether anybody on the desk is there to answer.</summary>
    public SupportPresenceState State
    {
        get
        {
            lock (_lock) return StateAt(timeProvider.GetUtcNow());
        }
    }

    /// <summary>What <paramref name="identity"/> chose, or <c>null</c> when they have set nothing.</summary>
    public SupportStaffStatus? StatusOf(string identity)
    {
        if (string.IsNullOrEmpty(identity)) return null;

        lock (_lock) return _staff.TryGetValue(identity, out var entry) ? entry.Status : null;
    }

    /// <summary>Records what <paramref name="identity"/> chose, which counts as acting.</summary>
    /// <remarks>Always raises <see cref="Changed"/>, so another page of the same person shows the choice.</remarks>
    public void Set(string identity, SupportStaffStatus status)
    {
        if (string.IsNullOrEmpty(identity)) return;

        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            _staff[identity] = (status, now);
            Recompute(now);
        }

        Raise();
    }

    /// <summary>Records that <paramref name="identity"/> did something in support. Nothing for somebody who has set no status.</summary>
    public void Acted(string identity)
    {
        if (string.IsNullOrEmpty(identity)) return;

        bool changed;
        lock (_lock)
        {
            if (!_staff.TryGetValue(identity, out var entry)) return;

            var now = timeProvider.GetUtcNow();
            _staff[identity] = entry with { ActedAt = now };
            changed = Recompute(now);
        }

        if (changed) Raise();
    }

    private SupportPresenceState StateAt(DateTimeOffset now)
    {
        if (_staff.Values.Any(x => IsOnline(x, now))) return SupportPresenceState.Online;

        return _staff.Values.Any(x => x.Status != SupportStaffStatus.Hidden) ? SupportPresenceState.Away : SupportPresenceState.Unknown;
    }

    private bool IsOnline((SupportStaffStatus Status, DateTimeOffset ActedAt) entry, DateTimeOffset now)
        => entry.Status == SupportStaffStatus.Online && now - entry.ActedAt < _idleAfter;

    private bool Recompute(DateTimeOffset now)
    {
        var state = StateAt(now);
        ScheduleIdle(now);

        if (state == _state) return false;

        _state = state;
        return true;
    }

    // One timer, for whoever goes idle first: nobody acting is a change too, and nothing else would notice it.
    private void ScheduleIdle(DateTimeOffset now)
    {
        _timer?.Dispose();
        _timer = null;

        var online = _staff.Values.Where(x => IsOnline(x, now)).ToArray();
        if (online.Length == 0) return;

        var next = online.Min(x => x.ActedAt + _idleAfter) - now;
        _timer = timeProvider.CreateTimer(_ => Lapse(), null, next < TimeSpan.Zero ? TimeSpan.Zero : next, Timeout.InfiniteTimeSpan);
    }

    private void Lapse()
    {
        bool changed;
        lock (_lock) changed = Recompute(timeProvider.GetUtcNow());

        if (changed) Raise();
    }

    private void Raise()
    {
        var handlers = Changed;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "A support presence handler threw. Only that listener missed the change.");
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
