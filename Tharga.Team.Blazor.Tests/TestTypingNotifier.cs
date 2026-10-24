namespace Tharga.Team.Blazor.Tests;

/// <summary>The toolkit's in-process notifier, as a component meets it: raised on the caller's thread, to every subscriber.</summary>
internal sealed class TestTypingNotifier : ISupportTypingNotifier
{
    public event EventHandler<SupportCaseTypingEventArgs> Typing;

    public void Notify(SupportCaseTypingEventArgs args) => Typing?.Invoke(this, args);

    public void Writes(string teamKey, string caseId, string identity, bool asSupport, string name = null) => Notify(new SupportCaseTypingEventArgs
    {
        TeamKey = teamKey,
        CaseId = caseId,
        Identity = identity,
        Name = name,
        AsSupport = asSupport
    });
}

/// <summary>A clock that moves only when told to, firing the timers that fall due.</summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, _now + dueTime);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;

        ManualTimer[] due;
        lock (_timers) due = [.. _timers.Where(x => !x.Done && x.Due <= _now)];
        foreach (var timer in due) timer.Fire();
    }

    private sealed class ManualTimer(ManualTime time, TimerCallback callback, object state, DateTimeOffset due) : ITimer
    {
        public DateTimeOffset Due { get; private set; } = due;

        public bool Done { get; private set; }

        public void Fire()
        {
            if (Done) return;
            Done = true;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = time.GetUtcNow() + dueTime;
            Done = false;
            return true;
        }

        public void Dispose() => Done = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
