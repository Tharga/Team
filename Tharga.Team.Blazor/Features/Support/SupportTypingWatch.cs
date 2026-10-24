namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// Who is writing in the case on screen, on the side facing the reader, for as long as their signals keep coming.
/// </summary>
/// <remarks>
/// The reader's own side is the right of the transcript, as <see cref="SupportTranscript"/> lays a case out, so
/// nobody sees dots for what they are writing themselves — and somebody both writing as a member and answering as
/// support, as a developer trying the sample does, sees each side from the other.
/// </remarks>
internal sealed class SupportTypingWatch : IDisposable
{
    /// <summary>How long a signal shows with no further one: past two report intervals, so one late signal does not make the dots blink.</summary>
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(5);

    private readonly ISupportTypingNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly Action _changed;
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Identity, bool AsSupport), (SupportCaseTypingEventArgs Writer, DateTimeOffset Until)> _writers = [];

    private string _teamKey;
    private string _caseId;
    private string _authorIdentity;
    private bool _authorOnRight;
    private ITimer _timer;

    /// <param name="notifier">Null when support typing is not registered; nobody is then ever shown writing.</param>
    /// <param name="time">The clock signals lapse by.</param>
    /// <param name="changed">Called, on whatever thread, when somebody starts or stops being shown.</param>
    public SupportTypingWatch(ISupportTypingNotifier notifier, TimeProvider time, Action changed)
    {
        _notifier = notifier;
        _time = time ?? TimeProvider.System;
        _changed = changed;

        if (_notifier != null) _notifier.Typing += OnTyping;
    }

    /// <summary>Who is writing now.</summary>
    public SupportCaseTypingEventArgs[] Writing
    {
        get
        {
            lock (_lock)
            {
                var now = _time.GetUtcNow();
                return [.. _writers.Values.Where(x => x.Until > now).Select(x => x.Writer)];
            }
        }
    }

    /// <summary>Whether a writer belongs on the side of whoever raised the case.</summary>
    public static bool FromAuthor(SupportCaseTypingEventArgs writer, string authorIdentity)
        => !writer.AsSupport && writer.Identity == authorIdentity;

    /// <summary>Watches another case, or none when <paramref name="caseId"/> is null; whoever was writing in the last one is forgotten.</summary>
    public void Follow(string teamKey, string caseId, string authorIdentity, bool authorOnRight)
    {
        lock (_lock)
        {
            if (_teamKey == teamKey && _caseId == caseId && _authorIdentity == authorIdentity && _authorOnRight == authorOnRight) return;

            _teamKey = teamKey;
            _caseId = caseId;
            _authorIdentity = authorIdentity;
            _authorOnRight = authorOnRight;
            _writers.Clear();
        }
    }

    /// <summary>Forgets everybody writing, because a message just arrived; whoever goes on writing shows again with their next signal.</summary>
    public void Clear()
    {
        lock (_lock) _writers.Clear();
    }

    private void OnTyping(object sender, SupportCaseTypingEventArgs args)
    {
        bool appeared;

        lock (_lock)
        {
            if (_caseId == null || args.CaseId != _caseId || args.TeamKey != _teamKey) return;
            if (FromAuthor(args, _authorIdentity) == _authorOnRight) return;

            var now = _time.GetUtcNow();
            var key = (args.Identity, args.AsSupport);
            appeared = !_writers.TryGetValue(key, out var shown) || shown.Until <= now;

            _writers[key] = (args, now + ShowFor);
            ScheduleLapse(now);
        }

        if (appeared) _changed?.Invoke();
    }

    private void ScheduleLapse(DateTimeOffset now)
    {
        _timer?.Dispose();
        _timer = null;
        if (_writers.Count == 0) return;

        var next = _writers.Values.Min(x => x.Until) - now;
        _timer = _time.CreateTimer(_ => Lapse(), null, next < TimeSpan.Zero ? TimeSpan.Zero : next, Timeout.InfiniteTimeSpan);
    }

    private void Lapse()
    {
        int lapsed;

        lock (_lock)
        {
            var now = _time.GetUtcNow();
            var gone = _writers.Where(x => x.Value.Until <= now).Select(x => x.Key).ToArray();
            foreach (var key in gone) _writers.Remove(key);

            lapsed = gone.Length;
            ScheduleLapse(now);
        }

        if (lapsed > 0) _changed?.Invoke();
    }

    public void Dispose()
    {
        if (_notifier != null) _notifier.Typing -= OnTyping;

        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _writers.Clear();
        }
    }
}
