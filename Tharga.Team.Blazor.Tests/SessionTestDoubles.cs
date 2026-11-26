using System.Security.Claims;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>Doubles shared by the server-side session tests (Tharga/Team#471); the people, teams and times are made up and fixed.</summary>
internal static class SessionTestData
{
    /// <summary>When every test starts, by <see cref="SessionClock"/>.</summary>
    public static readonly DateTimeOffset Start = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public const string UserKey = "user-1";
    public const string Subject = "subject-1";

    public static SessionOptions Options(Action<SessionOptions> configure = null)
    {
        // Set out in full so no test leans on a default; SessionOptionsTests holds the defaults.
        var options = new SessionOptions
        {
            Enabled = true,
            CacheDuration = TimeSpan.FromSeconds(30),
            ActivityInterval = TimeSpan.FromMinutes(1),
            IdleWarning = TimeSpan.FromMinutes(1),
            EndedPath = null,
            AdoptExistingCookies = true,
            OnError = SessionErrorBehavior.Allow,
            CookieScheme = "Cookies"
        };
        configure?.Invoke(options);
        return options;
    }

    public static IOptions<ThargaBlazorOptions> Wrap(SessionOptions sessions) => Microsoft.Extensions.Options.Options.Create(new ThargaBlazorOptions { Sessions = sessions });

    /// <summary>
    /// The domain service the evaluator reaches sessions through, over <paramref name="store"/>, auditing into <paramref name="audit"/> when given. The evaluator
    /// holds no store of its own: the ownership check and the audit-once rule are the domain's.
    /// </summary>
    public static UserSessionService Sessions(IUserSessionStore store, TimeProvider time, List<AuditEntry> audit = null, IUserSessionNotifier notifier = null)
        => new(store, new SessionUserService(), Mock.Of<ITeamPrincipalAccessor>(), time, auditLogger: audit == null ? null : CollectingAuditBackend.Composite(audit), notifier: notifier);

    /// <summary>A cookie principal carrying the library's session claim, as the cookie holds it after sign-in.</summary>
    public static ClaimsPrincipal WithSession(string sessionId, string subject = Subject, params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim(TeamClaimTypes.SessionId, sessionId, ClaimValueTypes.String, "Tharga.Team"), .. extra], "Test"));

    /// <summary>A cookie principal from before sessions existed: no session claim.</summary>
    public static ClaimsPrincipal WithoutSession(string subject = Subject, params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), .. extra], "Test"));

    public static UserSession Session(string id, DateTime signedInAt, string user = UserKey, string subject = null, string team = null, DateTime? lastActivity = null, SessionOrigin origin = SessionOrigin.SignIn)
        => new()
        {
            SessionId = id,
            UserKey = user,
            Subject = subject ?? (user == UserKey ? Subject : $"subject-{user}"),
            SignedInAt = signedInAt,
            Origin = origin,
            LastActivityAt = lastActivity ?? signedInAt,
            ActiveTeamKey = team
        };
}

/// <summary>A fixed, movable clock whose timers fire only when <see cref="Advance"/> passes them, so a test decides exactly when a wake happens.</summary>
internal sealed class SessionClock : TimeProvider
{
    private DateTimeOffset _now = SessionTestData.Start;

    public override DateTimeOffset GetUtcNow() => _now;

    public DateTime Now => _now.UtcDateTime;

    /// <summary>Every timer asked for, in the order created.</summary>
    public List<ManualTimer> Timers { get; } = [];

    /// <summary>Moves the clock on, then fires every armed timer that is due; a callback that re-arms its timer is not fired again until the clock moves again.</summary>
    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var timer in Timers.ToArray())
        {
            timer.FireIfDue(_now);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        Timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }
}

/// <summary>
/// A timer that records every <see cref="Change"/> and fires, once, when its clock passes the moment it was armed for. It refuses what the system timer
/// refuses: a due time beyond the longest it accepts, and any change once disposed.
/// </summary>
internal sealed class ManualTimer(SessionClock clock, TimerCallback callback, object state) : ITimer
{
    /// <summary>The longest due time <see cref="TimeProvider.System"/>'s timer accepts, in milliseconds.</summary>
    public const long LongestDueMilliseconds = 4294967294;

    /// <summary>Every call to <see cref="Change"/>, the creation's first.</summary>
    public List<(TimeSpan Due, TimeSpan Period)> Changes { get; } = [];

    /// <summary>When it fires next; null when it is not armed.</summary>
    public DateTimeOffset? DueAt { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>How long the last <see cref="Change"/> asked to wait; null when it never did or disarmed it.</summary>
    public TimeSpan? LastDue => Changes.Count == 0 || Changes[^1].Due == Timeout.InfiniteTimeSpan ? null : Changes[^1].Due;

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (dueTime != Timeout.InfiniteTimeSpan && (dueTime < TimeSpan.Zero || dueTime.TotalMilliseconds > LongestDueMilliseconds)) throw new ArgumentOutOfRangeException(nameof(dueTime));

        Changes.Add((dueTime, period));
        DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
        return true;
    }

    public void FireIfDue(DateTimeOffset now)
    {
        if (Disposed || DueAt == null || DueAt > now) return;

        DueAt = null;
        callback(state);
    }

    public void Dispose()
    {
        Disposed = true;
        DueAt = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>A circuit the view is tested against: the state is set by the test, and every call the view makes is counted.</summary>
internal sealed class FakeSessionCircuit : ISessionCircuit
{
    public bool Active { get; set; } = true;

    public SessionCircuitState State { get; set; } = SessionCircuitState.Watching;

    public DateTime? IdleAt { get; set; }

    public TimeSpan? Remaining { get; set; }

    public string EndedLocation { get; set; }

    public event Action Changed;

    public event Action Activated;

    public int Subscribers => Changed?.GetInvocationList().Length ?? 0;

    public int ActivatedSubscribers => Activated?.GetInvocationList().Length ?? 0;

    public int Attached { get; private set; }

    /// <summary>Whether a view holds the one asking place (<see cref="TryAttach"/>).</summary>
    public bool Asked { get; private set; }

    public int Stays { get; private set; }

    public int SignOuts { get; private set; }

    public int Checks { get; private set; }

    /// <summary>What <see cref="StartAsync"/> does; by default it completes at once.</summary>
    public Func<Task> OnStart { get; set; }

    public int StartCalls { get; private set; }

    public int Disposals { get; private set; }

    public Task StartAsync()
    {
        StartCalls++;
        return OnStart?.Invoke() ?? Task.CompletedTask;
    }

    public IDisposable Attach()
    {
        Attached++;
        return new Detach(this);
    }

    public IDisposable TryAttach()
    {
        if (Asked) return null;

        Asked = true;
        var attachment = Attach();
        return new Unask(this, attachment);
    }

    /// <summary>The watch found a session after the view arrived, as the real one does when the sign-in state is read late.</summary>
    public void Activate()
    {
        Active = true;
        Activated?.Invoke();
    }

    public Task StayAsync()
    {
        Stays++;
        Set(SessionCircuitState.Watching);
        return Task.CompletedTask;
    }

    public Task SignOutAsync()
    {
        SignOuts++;
        EndedLocation = "/session-ended?reason=signedout";
        Set(SessionCircuitState.Ended);
        return Task.CompletedTask;
    }

    public Task CheckAsync()
    {
        Checks++;
        return Task.CompletedTask;
    }

    /// <summary>Moves to <paramref name="state"/> and tells the view, as the real circuit does.</summary>
    public void Set(SessionCircuitState state, TimeSpan? remaining = null, string endedLocation = null)
    {
        State = state;
        Remaining = state == SessionCircuitState.Warning ? remaining : null;
        IdleAt = state == SessionCircuitState.Warning ? SessionTestData.Start.UtcDateTime + (remaining ?? TimeSpan.Zero) : null;
        if (endedLocation != null) EndedLocation = endedLocation;
        Changed?.Invoke();
    }

    public void Dispose() => Disposals++;

    private sealed class Unask(FakeSessionCircuit circuit, IDisposable attachment) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;

            _done = true;
            circuit.Asked = false;
            attachment.Dispose();
        }
    }

    private sealed class Detach(FakeSessionCircuit circuit) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;

            _done = true;
            circuit.Attached--;
        }
    }
}

internal sealed class CollectingAuditBackend(List<AuditEntry> entries) : IAuditLogger
{
    public void Log(AuditEntry entry) => entries.Add(entry);

    public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());

    public static CompositeAuditLogger Composite(List<AuditEntry> entries) => new([new CollectingAuditBackend(entries)], Microsoft.Extensions.Options.Options.Create(new AuditOptions()));
}

/// <summary>Answers the one-argument call only; the interface's default for the two-argument one forwards to it, so either finds the user.</summary>
internal sealed class SessionUserService(string key = SessionTestData.UserKey) : IUserService
{
    private sealed record User(string Key) : IUser
    {
        public string Identity => "alice";
        public string EMail => "alice@example.com";
    }

    public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => Task.FromResult<IUser>(key == null ? null : new User(key));

    public IAsyncEnumerable<IUser> GetAsync() => throw new NotSupportedException();

    public Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;

    public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
}

/// <summary>The host's numbers, per team, counting how often it is asked and optionally failing.</summary>
internal sealed class TestSessionPolicy : ISessionPolicy
{
    private readonly Dictionary<string, SessionLimits> _byTeam = new();

    public SessionLimits Default { get; set; } = SessionLimits.None;

    public Exception Failure { get; set; }

    public List<(string SessionId, string TeamKey)> Asked { get; } = [];

    public TestSessionPolicy For(string teamKey, SessionLimits limits)
    {
        _byTeam[teamKey] = limits;
        return this;
    }

    public Task<SessionLimits> GetLimitsAsync(UserSession session, string teamKey, CancellationToken cancellationToken = default)
    {
        Asked.Add((session.SessionId, teamKey));
        if (Failure != null) throw Failure;

        return Task.FromResult(teamKey != null && _byTeam.TryGetValue(teamKey, out var limits) ? limits : Default);
    }
}

/// <summary>
/// The store's contract in memory, so two evaluators ("instances") can share it: an end that is true once, activity throttled by the stored stamp, and
/// entering a team exclusively with the newest sign-in winning. It is the contract <c>UserSessionStoreTests</c> holds the real store to.
/// </summary>
internal sealed class InMemorySessionStore : IUserSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UserSession> _sessions = new();

    public Exception Failure { get; set; }

    /// <summary>When set, a read does not complete until the test completes it, so a test can hold a check in flight.</summary>
    public TaskCompletionSource Gate { get; set; }

    /// <summary>The synchronization context the last read started on, so a test can tell whose thread a check ran on.</summary>
    public SynchronizationContext ReadContext { get; private set; }

    public List<UserSession> Started { get; } = [];

    public int Reads { get; private set; }

    public List<(string SessionId, SessionEndReason Reason)> Ends { get; } = [];

    public List<(string SessionId, DateTime At, string TeamKey, TimeSpan MinInterval)> Activity { get; } = [];

    public List<(string SessionId, string TeamKey)> Entries { get; } = [];

    public int Calls { get; private set; }

    public UserSession Get(string id)
    {
        lock (_gate) return _sessions.GetValueOrDefault(id);
    }

    public void Put(UserSession session)
    {
        lock (_gate) _sessions[session.SessionId] = session;
    }

    private void Enter()
    {
        Calls++;
        if (Failure != null) throw Failure;
    }

    public Task StartAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Started.Add(session);
            _sessions[session.SessionId] = session;
        }

        return Task.CompletedTask;
    }

    public async Task<UserSession> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        UserSession session;
        var gate = Gate;
        lock (_gate)
        {
            Enter();
            Reads++;
            ReadContext = SynchronizationContext.Current;
            session = _sessions.GetValueOrDefault(sessionId);
        }

        if (gate != null) await gate.Task;

        return session;
    }

    public Task<bool> RecordActivityAsync(string sessionId, DateTime at, string teamKey, TimeSpan minInterval, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Activity.Add((sessionId, at, teamKey, minInterval));
            if (!_sessions.TryGetValue(sessionId, out var session) || session.EndedAt != null || at - session.LastActivityAt < minInterval || at < session.LastActivityAt) return Task.FromResult(false);

            _sessions[sessionId] = session with { LastActivityAt = at, ActiveTeamKey = teamKey };
            return Task.FromResult(true);
        }
    }

    public Task<bool> EndAsync(string sessionId, SessionEndReason reason, DateTime at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Ends.Add((sessionId, reason));
            if (!_sessions.TryGetValue(sessionId, out var session) || session.EndedAt != null) return Task.FromResult(false);

            _sessions[sessionId] = session with { EndedAt = at, EndReason = reason };
            return Task.FromResult(true);
        }
    }

    public Task<int> EndOthersAsync(string userKey, string exceptSessionId, DateTime at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            var others = _sessions.Values.Where(x => x.UserKey == userKey && x.SessionId != exceptSessionId && x.EndedAt == null).ToArray();
            foreach (var other in others) _sessions[other.SessionId] = other with { EndedAt = at, EndReason = SessionEndReason.Ended };
            return Task.FromResult(others.Length);
        }
    }

    public Task<SessionTeamEntry> EnterTeamExclusiveAsync(string sessionId, string teamKey, DateTime at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Entries.Add((sessionId, teamKey));
            var caller = _sessions[sessionId];
            var inTeam = _sessions.Values.Where(x => x.UserKey == caller.UserKey && x.SessionId != sessionId && x.EndedAt == null && x.ActiveTeamKey == teamKey).ToArray();

            if (inTeam.Any(x => x.SignedInAt > caller.SignedInAt))
            {
                _sessions[sessionId] = caller with { EndedAt = at, EndReason = SessionEndReason.NewerSignIn };
                return Task.FromResult(new SessionTeamEntry([], true));
            }

            foreach (var older in inTeam) _sessions[older.SessionId] = older with { EndedAt = at, EndReason = SessionEndReason.NewerSignIn };
            _sessions[sessionId] = caller with { ActiveTeamKey = teamKey };
            return Task.FromResult(new SessionTeamEntry(inTeam, false));
        }
    }
}

/// <summary>A logger that keeps what it was asked to write; Moq cannot proxy a logger of an internal type.</summary>
internal sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<(Microsoft.Extensions.Logging.LogLevel Level, Exception Exception)> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Entries.Add((logLevel, exception));
}
