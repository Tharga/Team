namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// Whether a member writing to a person has waited long enough to be told somebody will get back to them.
/// </summary>
/// <remarks>
/// Worked out from the conversation as read and the clock, and stored nowhere: the wait starts at the member's first
/// message after anybody else last wrote, so a reload shows the notice again and an answer takes it away.
/// </remarks>
internal sealed class SupportNoAnswerWatch : IDisposable
{
    /// <summary>How long a member waits for a person before they are told, unless the host says otherwise.</summary>
    public static readonly TimeSpan DefaultAfter = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time;
    private readonly Action _due;
    private readonly Lock _lock = new();
    private DateTime? _since;
    private ITimer _timer;

    /// <param name="time">The clock the wait is measured by.</param>
    /// <param name="due">Called, on whatever thread, when the wait runs out.</param>
    public SupportNoAnswerWatch(TimeProvider time, Action due)
    {
        _time = time ?? TimeProvider.System;
        _due = due;
    }

    /// <summary>How long the member waits before they are told.</summary>
    public TimeSpan After { get; set; } = DefaultAfter;

    /// <summary>When the member's first message nobody has answered yet was sent, or null when nothing waits.</summary>
    /// <remarks>Notes about the case are not answers; anybody but the author writing is.</remarks>
    public static DateTime? WaitingSince(IEnumerable<SupportMessage> messages, string authorIdentity)
    {
        DateTime? since = null;

        foreach (var message in (messages ?? []).Where(x => x.Kind != SupportMessageKind.System).OrderBy(x => x.Sequence))
        {
            var fromAuthor = message.Kind == SupportMessageKind.User && !message.AsSupport && message.AuthorIdentity == authorIdentity;

            if (fromAuthor) since ??= message.SentAt;
            else since = null;
        }

        return since;
    }

    /// <summary>Follows the conversation as just read, from whoever raised it.</summary>
    public void Follow(IEnumerable<SupportMessage> messages, string authorIdentity)
    {
        lock (_lock)
        {
            _since = WaitingSince(messages, authorIdentity);
            Schedule();
        }
    }

    /// <summary>Nothing waits, as in a new conversation.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _since = null;
            Schedule();
        }
    }

    /// <summary>Whether to tell the member now: the wait has run out, or support is away so there is no point in waiting.</summary>
    public bool Shows(SupportPresenceState presence)
    {
        lock (_lock)
        {
            return _since != null && (presence == SupportPresenceState.Away || _time.GetUtcNow().UtcDateTime - _since.Value >= After);
        }
    }

    private void Schedule()
    {
        _timer?.Dispose();
        _timer = null;
        if (_since == null) return;

        var left = _since.Value + After - _time.GetUtcNow().UtcDateTime;
        if (left <= TimeSpan.Zero) return;

        _timer = _time.CreateTimer(_ => _due?.Invoke(), null, left, Timeout.InfiniteTimeSpan);
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
