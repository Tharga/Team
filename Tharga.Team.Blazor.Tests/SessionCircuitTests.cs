using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// One circuit's watch over its server-side session (Tharga/Team#471): a single timer armed to the next moment that matters, the store as the only truth, a
/// "Still here?" warning only for a view that can show it and only to somebody who is not working, and an end that is audited once.
/// </summary>
/// <remarks>
/// Time is <see cref="SessionClock"/>, whose timers fire only when a test advances the clock past them. Unless a test says otherwise the host allows 10 minutes
/// idle, warns 2 minutes before (so the warning is due at 8 minutes), and records activity at most once a minute.
/// </remarks>
public class SessionCircuitTests
{
    private const string EndedPath = "/session-ended";

    private readonly SessionClock _clock = new();
    private readonly InMemorySessionStore _store = new();
    private readonly TestSessionPolicy _policy = new() { Default = new SessionLimits { IdleTimeout = TimeSpan.FromMinutes(10) } };
    private readonly List<AuditEntry> _audit = [];
    private readonly SessionCache _cache;
    private readonly ServerAuthenticationStateProvider _auth = new();
    private readonly List<UserSessionEndedEventArgs> _heard = [];
    private readonly List<SessionCircuitState> _states = [];
    private bool _withPolicy = true;

    public SessionCircuitTests()
    {
        _cache = new SessionCache(_clock);
        _cache.SessionEnded += (_, e) => _heard.Add(e);
        _store.Put(Session("s1", _clock.Now));
    }

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    private SessionOptions Configured(Action<SessionOptions> configure = null) => Options(o =>
    {
        o.EndedPath = EndedPath;
        o.IdleWarning = Min(2);
        configure?.Invoke(o);
    });

    private SessionCircuit Build(string sessionId = "s1", ServerAuthenticationStateProvider auth = null, ClaimsPrincipal principal = null, Action<SessionOptions> configure = null)
    {
        auth ??= _auth;
        auth.SetAuthenticationState(Task.FromResult(new AuthenticationState(principal ?? WithSession(sessionId))));

        var options = Configured(configure);
        var evaluator = new SessionEvaluator(_cache, Wrap(options), _clock, _withPolicy ? _policy : null, Sessions(_store, _clock, _audit, _cache));
        var circuit = new SessionCircuit(evaluator, auth, auth, _cache, Wrap(options), _clock);
        circuit.Changed += () => _states.Add(circuit.State);
        return circuit;
    }

    private async Task<SessionCircuit> StartedAsync(bool view = false, Action<SessionOptions> configure = null)
    {
        var circuit = Build(configure: configure);
        if (view) circuit.Attach();

        await circuit.StartAsync();
        await circuit.SettledAsync();
        return circuit;
    }

    private async Task AdvanceAsync(SessionCircuit circuit, TimeSpan by)
    {
        _clock.Advance(by);
        await circuit.SettledAsync();
    }

    private ManualTimer TheTimer => Assert.Single(_clock.Timers);

    private void Hint(string userKey = UserKey, SessionEndReason reason = SessionEndReason.Ended)
        => _cache.Notify(new UserSessionEndedEventArgs { UserKey = userKey, Reason = reason, FromRemote = true });

    // ---- nothing to watch

    [Fact]
    public async Task StartAsync_WhenSessionsAreNotActive_ShouldWatchNothingAndTouchNoStore()
    {
        _withPolicy = false;

        var circuit = await StartedAsync(view: true);

        Assert.False(circuit.Active);
        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Equal(0, _store.Calls);
        Assert.All(_clock.Timers, t => Assert.Null(t.DueAt));
    }

    [Fact]
    public async Task StartAsync_WhenTheCallerHasNoSession_ShouldWatchNothing()
    {
        var circuit = Build(principal: WithoutSession());

        await circuit.StartAsync();
        await circuit.SettledAsync();

        Assert.False(circuit.Active);
        Assert.Equal(0, _store.Calls);
        Assert.All(_clock.Timers, t => Assert.Null(t.DueAt));
    }

    [Fact]
    public async Task StartAsync_WhenTheCallerIsAnonymous_ShouldWatchNothing()
    {
        var circuit = Build(principal: new ClaimsPrincipal(new ClaimsIdentity()));

        await circuit.StartAsync();
        await circuit.SettledAsync();

        Assert.False(circuit.Active);
        Assert.Equal(0, _store.Calls);
    }

    /// <summary>With no limit there is nothing to time, but the circuit still listens for a hint that its session ended elsewhere.</summary>
    [Fact]
    public async Task StartAsync_WhenTheHostSetsNoLimit_ShouldArmNoTimerButStillListen()
    {
        _policy.Default = SessionLimits.None;
        var circuit = await StartedAsync(view: true);
        _store.Put(Session("s1", _clock.Now) with { EndedAt = _clock.Now, EndReason = SessionEndReason.Ended });

        Assert.True(circuit.Active);
        Assert.All(_clock.Timers, t => Assert.Null(t.DueAt));

        Hint();
        await circuit.SettledAsync();

        Assert.Equal(SessionCircuitState.Ended, circuit.State);
    }

    [Fact]
    public async Task StartAsync_ShouldNeitherAdoptNorRecordActivity()
    {
        var circuit = await StartedAsync(view: true);
        await AdvanceAsync(circuit, Min(8));

        Assert.Empty(_store.Started);
        Assert.Empty(_store.Activity);
        Assert.Empty(_store.Entries);
    }

    // ---- one timer, armed to the next moment

    [Fact]
    public async Task StartAsync_ShouldArmOneTimerToTheWarning()
    {
        await StartedAsync();

        Assert.Equal(Min(8), TheTimer.LastDue);
        Assert.False(TheTimer.Disposed);
    }

    [Fact]
    public async Task EveryWake_ShouldReuseTheOneTimer()
    {
        var circuit = await StartedAsync();

        await AdvanceAsync(circuit, Min(8));
        await AdvanceAsync(circuit, Min(2) + TimeSpan.FromSeconds(1));

        Assert.Single(_clock.Timers);
    }

    [Fact]
    public async Task AWake_ShouldReadTheStoreAfresh_NotTheRememberedSession()
    {
        var circuit = await StartedAsync(view: true);
        _cache.SetSession(Session("s1", SessionTestData.Start.UtcDateTime), Min(60));
        var reads = _store.Reads;

        await AdvanceAsync(circuit, Min(8));

        Assert.True(_store.Reads > reads);
    }

    // ---- the warning

    [Fact]
    public async Task AtTheWarning_WithAViewAttached_ShouldWarnWithTheTimeLeft()
    {
        var circuit = await StartedAsync(view: true);

        await AdvanceAsync(circuit, Min(8));

        Assert.Equal(SessionCircuitState.Warning, circuit.State);
        Assert.Equal(SessionTestData.Start.UtcDateTime.AddMinutes(10), circuit.IdleAt);
        Assert.Equal(Min(2), circuit.Remaining);
        Assert.Equal([SessionCircuitState.Warning], _states);
        Assert.Equal(Min(2) + TimeSpan.FromSeconds(1), TheTimer.LastDue);
    }

    /// <summary>Nobody is warned who cannot see the warning; the session still ends on time.</summary>
    [Fact]
    public async Task AtTheWarning_WithNoViewAttached_ShouldNotWarn_AndWaitForTheIdleMoment()
    {
        var circuit = await StartedAsync(view: false);

        await AdvanceAsync(circuit, Min(8));

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Empty(_states);
        Assert.Equal(Min(2) + TimeSpan.FromSeconds(1), TheTimer.LastDue);
    }

    [Fact]
    public async Task AViewAttachedInsideTheWarningWindow_ShouldBeWarnedAtOnce()
    {
        var circuit = await StartedAsync(view: false);
        await AdvanceAsync(circuit, Min(9));

        using var view = circuit.Attach();
        await circuit.SettledAsync();

        Assert.Equal(SessionCircuitState.Warning, circuit.State);
        Assert.Equal(Min(1), circuit.Remaining);
    }

    [Fact]
    public async Task StartingInsideTheWarningWindow_WithAViewAttached_ShouldWarnAtOnce()
    {
        _clock.Advance(Min(9));

        var circuit = await StartedAsync(view: true);

        Assert.Equal(SessionCircuitState.Warning, circuit.State);
        Assert.Equal(Min(1), circuit.Remaining);
        Assert.Equal(Min(1) + TimeSpan.FromSeconds(1), TheTimer.LastDue);
    }

    /// <summary>Somebody working in another tab, or on another instance, is never asked: the wake reads the store's stamp, which is theirs.</summary>
    [Fact]
    public async Task AtTheWarning_WhenSomebodyWasActiveSince_ShouldNotAsk_AndMoveTheTimerOn()
    {
        var circuit = await StartedAsync(view: true);
        _clock.Advance(Min(5));
        _store.Put(Session("s1", SessionTestData.Start.UtcDateTime, lastActivity: _clock.Now));

        await AdvanceAsync(circuit, Min(3));

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Empty(_states);
        Assert.Equal(Min(5), TheTimer.LastDue);
    }

    [Fact]
    public async Task AWarning_WhenActivityIsRecordedElsewhere_ShouldClearOnTheNextLook()
    {
        var circuit = await StartedAsync(view: true);
        await AdvanceAsync(circuit, Min(8));
        _store.Put(Session("s1", SessionTestData.Start.UtcDateTime, lastActivity: _clock.Now));

        await circuit.CheckAsync();

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Equal([SessionCircuitState.Warning, SessionCircuitState.Watching], _states);
        Assert.Equal(Min(8), TheTimer.LastDue);
    }

    // ---- staying

    [Fact]
    public async Task StayAsync_WhileWarned_ShouldRecordActivityNow_AndStartOver()
    {
        var circuit = await StartedAsync(view: true);
        await AdvanceAsync(circuit, Min(8));
        _cache.SetSession(Session("s1", SessionTestData.Start.UtcDateTime), Min(60));

        await circuit.StayAsync();

        Assert.Equal(TimeSpan.Zero, Assert.Single(_store.Activity).MinInterval);
        Assert.Equal(_clock.Now, _store.Get("s1").LastActivityAt);
        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Equal([SessionCircuitState.Warning, SessionCircuitState.Watching], _states);
        Assert.Equal(Min(8), TheTimer.LastDue);
        Assert.False(_cache.TryGetSession("s1", out _));
        Assert.Empty(_audit);
    }

    /// <summary>Only an answer to the question writes; a stray "yes" is not activity.</summary>
    [Fact]
    public async Task StayAsync_WhenNotWarned_ShouldWriteNothing()
    {
        var circuit = await StartedAsync(view: true);

        await circuit.StayAsync();

        Assert.Empty(_store.Activity);
        Assert.Equal(SessionCircuitState.Watching, circuit.State);
    }

    // ---- ending

    [Fact]
    public async Task AtTheIdleMoment_ShouldEndTheSessionAsIdle_AuditedOnce()
    {
        var circuit = await StartedAsync(view: true);
        await AdvanceAsync(circuit, Min(8));

        await AdvanceAsync(circuit, Min(2) + TimeSpan.FromSeconds(1));

        Assert.Equal(SessionCircuitState.Ended, circuit.State);
        Assert.Equal($"{EndedPath}?reason=idle", circuit.EndedLocation);
        Assert.Equal(SessionEndReason.Idle, _store.Get("s1").EndReason);
        var entry = Assert.Single(_audit);
        Assert.Equal("session-ended", entry.Action);
        Assert.Equal([SessionCircuitState.Warning, SessionCircuitState.Ended], _states);
    }

    [Fact]
    public async Task WhenEnded_ShouldStopTheTimer_AndSignTheCircuitOut()
    {
        var circuit = await StartedAsync(view: true);

        await AdvanceAsync(circuit, Min(10) + TimeSpan.FromSeconds(1));

        Assert.Null(TheTimer.DueAt);
        var state = await _auth.GetAuthenticationStateAsync();
        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task WhenEnded_WithNoEndedPath_ShouldGoToTheStartPageWithTheReason()
    {
        var circuit = await StartedAsync(configure: o => o.EndedPath = null);

        await AdvanceAsync(circuit, Min(10) + TimeSpan.FromSeconds(1));

        Assert.Equal("/?reason=idle", circuit.EndedLocation);
    }

    [Fact]
    public async Task AtTheMaxAge_ShouldEndTheSession_WithoutEverWarning()
    {
        _policy.Default = new SessionLimits { IdleTimeout = Min(10), MaxAge = Min(5) };
        var circuit = await StartedAsync(view: true);
        Assert.Equal(Min(5) + TimeSpan.FromSeconds(1), TheTimer.LastDue);

        await AdvanceAsync(circuit, Min(5) + TimeSpan.FromSeconds(1));

        Assert.Equal(SessionCircuitState.Ended, circuit.State);
        Assert.Equal($"{EndedPath}?reason=maxage", circuit.EndedLocation);
        Assert.Equal([SessionCircuitState.Ended], _states);
        Assert.Single(_audit);
    }

    [Fact]
    public async Task StartingWithASessionAlreadyEndedElsewhere_ShouldEndAtOnce_WithoutAuditingAgain()
    {
        _store.Put(Session("s1", _clock.Now) with { EndedAt = _clock.Now, EndReason = SessionEndReason.NewerSignIn });

        var circuit = await StartedAsync(view: true);

        Assert.Equal(SessionCircuitState.Ended, circuit.State);
        Assert.Equal($"{EndedPath}?reason=newersignin", circuit.EndedLocation);
        Assert.Empty(_audit);
        Assert.All(_clock.Timers, t => Assert.Null(t.DueAt));
    }

    [Fact]
    public async Task StartingWithASessionAlreadyOverItsLimit_ShouldEndItAtOnce_AuditedOnce()
    {
        _clock.Advance(Min(30));

        var circuit = await StartedAsync(view: true);

        Assert.Equal(SessionCircuitState.Ended, circuit.State);
        Assert.Equal($"{EndedPath}?reason=idle", circuit.EndedLocation);
        Assert.Single(_audit);
    }

    /// <summary>An end is final: nothing the circuit hears afterwards reads or writes anything.</summary>
    [Fact]
    public async Task AfterTheEnd_NothingElseIsReadOrWritten()
    {
        var circuit = await StartedAsync(view: true);
        await AdvanceAsync(circuit, Min(10) + TimeSpan.FromSeconds(1));
        var calls = _store.Calls;

        Hint();
        await circuit.CheckAsync();
        await circuit.StayAsync();
        await circuit.SignOutAsync();
        await AdvanceAsync(circuit, Min(60));

        Assert.Equal(calls, _store.Calls);
        Assert.Equal(SessionCircuitState.Ended, circuit.State);
        Assert.Single(_audit);
    }

    // ---- signing out

    [Fact]
    public async Task SignOutAsync_ShouldEndTheSessionAsSignedOut_AuditedOnce_AndTellThePerson()
    {
        var circuit = await StartedAsync(view: true);

        await circuit.SignOutAsync();

        Assert.Equal(SessionCircuitState.Ended, circuit.State);
        Assert.Equal($"{EndedPath}?reason=signedout", circuit.EndedLocation);
        Assert.Equal(SessionEndReason.SignedOut, _store.Get("s1").EndReason);
        Assert.Equal("signout", Assert.Single(_audit).Action);
        var heard = Assert.Single(_heard);
        Assert.Equal((UserKey, SessionEndReason.SignedOut, false), (heard.UserKey, heard.Reason, heard.FromRemote));
        var state = await _auth.GetAuthenticationStateAsync();
        Assert.False(state.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task SignOutAsync_Twice_ShouldAuditAndAnnounceOnce()
    {
        var circuit = await StartedAsync(view: true);

        await circuit.SignOutAsync();
        await circuit.SignOutAsync();

        Assert.Single(_audit);
        Assert.Single(_heard);
    }

    /// <summary>Signing out of one session is not signing out of another: the other circuit is told, looks, and carries on.</summary>
    [Fact]
    public async Task SigningOutOneSession_ShouldLeaveAnotherSessionOfTheSamePersonGoing()
    {
        _store.Put(Session("s2", _clock.Now));
        var first = await StartedAsync();
        var second = Build("s2", new ServerAuthenticationStateProvider());
        await second.StartAsync();
        await second.SettledAsync();

        await first.SignOutAsync();
        await second.SettledAsync();

        Assert.Equal(SessionCircuitState.Ended, first.State);
        Assert.Equal(SessionCircuitState.Watching, second.State);
        Assert.Null(_store.Get("s2").EndedAt);
    }

    // ---- hints

    [Fact]
    public async Task AHintForThisPerson_ShouldCauseACheck_AndEndWhenTheStoreSaysSo()
    {
        var circuit = await StartedAsync(view: true);
        _store.Put(Session("s1", _clock.Now) with { EndedAt = _clock.Now, EndReason = SessionEndReason.NewerSignIn });

        Hint(reason: SessionEndReason.NewerSignIn);
        await circuit.SettledAsync();

        Assert.Equal(SessionCircuitState.Ended, circuit.State);
        Assert.Equal($"{EndedPath}?reason=newersignin", circuit.EndedLocation);
        Assert.Empty(_audit);
    }

    /// <summary>The reason on the wire is the one the store holds, not the hint's: a hint decides nothing.</summary>
    [Fact]
    public async Task AHint_ShouldDecideNothing_WhenTheStoreSaysTheSessionLasts()
    {
        var circuit = await StartedAsync(view: true);
        var reads = _store.Reads;

        Hint(reason: SessionEndReason.SignedOut);
        await circuit.SettledAsync();

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.True(_store.Reads > reads);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task AHintForAnotherPerson_ShouldCauseNoCheck()
    {
        var circuit = await StartedAsync(view: true);
        var calls = _store.Calls;

        Hint("user-2");
        await circuit.SettledAsync();

        Assert.Equal(calls, _store.Calls);
    }

    // ---- failure

    /// <summary>A database blip must not end anybody's circuit, however long it lasts; the next look is a minute later.</summary>
    [Fact]
    public async Task WhenTheStoreFailsAtAWake_ShouldNotEnd_AndRetryAfterTheActivityInterval()
    {
        var circuit = await StartedAsync(view: true);
        _store.Failure = new InvalidOperationException("the session database is down");

        await AdvanceAsync(circuit, Min(8));

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Empty(_audit);
        Assert.Equal(Min(1), TheTimer.LastDue);

        await AdvanceAsync(circuit, Min(60));

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Equal(Min(1), TheTimer.LastDue);
    }

    [Fact]
    public async Task WhenTheStoreRecovers_TheWatchCarriesOn()
    {
        var circuit = await StartedAsync(view: true);
        _store.Failure = new InvalidOperationException("the session database is down");
        await AdvanceAsync(circuit, Min(8));
        _store.Failure = null;

        await AdvanceAsync(circuit, Min(1));

        Assert.Equal(SessionCircuitState.Warning, circuit.State);
        Assert.Equal(Min(1), circuit.Remaining);
    }

    [Fact]
    public async Task WhenTheStoreFailsAtStart_ShouldNotThrow_AndRetryAfterTheActivityInterval()
    {
        _store.Failure = new InvalidOperationException("the session database is down");

        var circuit = await StartedAsync(view: true);

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Equal(Min(1), TheTimer.LastDue);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task WhenThePolicyFails_ShouldNotEnd_Too()
    {
        _policy.Failure = new InvalidOperationException("the policy is down");

        var circuit = await StartedAsync(view: true);
        await AdvanceAsync(circuit, Min(60));

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Empty(_audit);
    }

    /// <summary>A session that is not there (or not the caller's) is the request check's to refuse, not the circuit's to end.</summary>
    [Fact]
    public async Task WhenTheStoreHasNoSuchSession_ShouldNotEnd_AndLookAgainLater()
    {
        _policy.Default = new SessionLimits { IdleTimeout = Min(10) };
        var circuit = Build("missing");

        await circuit.StartAsync();
        await circuit.SettledAsync();

        Assert.Equal(SessionCircuitState.Watching, circuit.State);
        Assert.Empty(_audit);
        Assert.Equal(Min(1), TheTimer.LastDue);
    }

    // ---- coalescing

    [Fact]
    public async Task ChecksThatArriveTogether_ShouldBeCoalesced_NotRunInParallel()
    {
        var circuit = await StartedAsync(view: true);
        var reads = _store.Reads;
        _store.Gate = new TaskCompletionSource();

        var checks = new[] { circuit.CheckAsync(), circuit.CheckAsync(), circuit.CheckAsync(), circuit.CheckAsync() };
        _store.Gate.SetResult();
        await Task.WhenAll(checks);

        Assert.InRange(_store.Reads - reads, 1, 2);
    }

    // ---- disposing

    [Fact]
    public async Task Dispose_ShouldStopTheTimer_AndStopListening()
    {
        var circuit = await StartedAsync(view: true);
        var calls = _store.Calls;

        circuit.Dispose();
        Hint();
        await circuit.CheckAsync();
        _clock.Advance(Min(60));
        await circuit.SettledAsync();

        Assert.True(TheTimer.Disposed);
        Assert.Equal(calls, _store.Calls);
        Assert.Empty(_states);
    }

    [Fact]
    public async Task Dispose_ShouldBeSafeToCallTwice()
    {
        var circuit = await StartedAsync();

        circuit.Dispose();
        var exception = Record.Exception(circuit.Dispose);

        Assert.Null(exception);
    }

    // ---- the deadlines, which are pure

    public static TheoryData<int, int, int, int?> Warnings => new()
    {
        // idle, warning, interval (seconds) -> when the warning is due after the last activity; null for none
        { 600, 60, 60, 540 },
        { 600, 300, 60, 300 },
        // Longer than idle - 2 x interval: clamped to it, so somebody who answers always has time to be heard.
        { 600, 540, 60, 120 },
        { 180, 60, 60, 120 },
        { 180, 600, 60, 120 },
        // Nothing left once clamped: no warning.
        { 120, 60, 60, null },
        { 600, 60, 300, null },
        { 60, 60, 60, null },
        // Zero asks nobody.
        { 600, 0, 60, null }
    };

    [Theory]
    [MemberData(nameof(Warnings))]
    public void Deadlines_ShouldPlaceTheWarning_AsTheOptionsClampIt(int idle, int warning, int interval, int? expected)
    {
        var session = Session("s1", SessionTestData.Start.UtcDateTime);
        var limits = new SessionLimits { IdleTimeout = TimeSpan.FromSeconds(idle) };
        var options = Options(o =>
        {
            o.IdleWarning = TimeSpan.FromSeconds(warning);
            o.ActivityInterval = TimeSpan.FromSeconds(interval);
        });

        var deadlines = SessionDeadlines.Of(session, limits, options);

        Assert.Equal(SessionTestData.Start.UtcDateTime.AddSeconds(idle), deadlines.IdleAt);
        Assert.Equal(expected == null ? null : SessionTestData.Start.UtcDateTime.AddSeconds(expected.Value), deadlines.WarnAt);
        Assert.Null(deadlines.MaxAgeAt);
    }

    [Fact]
    public void Deadlines_ShouldCountIdleFromTheLastActivity_AndMaxAgeFromTheSignIn()
    {
        var signedIn = SessionTestData.Start.UtcDateTime;
        var session = Session("s1", signedIn, lastActivity: signedIn.AddMinutes(30));
        var limits = new SessionLimits { IdleTimeout = Min(10), MaxAge = Min(480) };

        var deadlines = SessionDeadlines.Of(session, limits, Options(o => o.IdleWarning = Min(1)));

        Assert.Equal(signedIn.AddMinutes(40), deadlines.IdleAt);
        Assert.Equal(signedIn.AddMinutes(39), deadlines.WarnAt);
        Assert.Equal(signedIn.AddMinutes(480), deadlines.MaxAgeAt);
    }

    [Fact]
    public void Deadlines_WithNoLimits_ShouldHaveNone()
    {
        var deadlines = SessionDeadlines.Of(Session("s1", SessionTestData.Start.UtcDateTime), SessionLimits.None, Options());

        Assert.Equal(new SessionDeadlines(null, null, null), deadlines);
        Assert.Null(deadlines.NextWake(false));
        Assert.Null(deadlines.NextWake(true));
    }

    /// <summary>When max age ends the session no later than idle would, staying would not help, so nobody is asked.</summary>
    [Theory]
    [InlineData(600, false)]
    [InlineData(599, false)]
    [InlineData(601, true)]
    public void Deadlines_ShouldNotWarn_WhenMaxAgeEndsTheSessionNoLaterThanIdle(int maxAgeSeconds, bool warns)
    {
        var limits = new SessionLimits { IdleTimeout = TimeSpan.FromSeconds(600), MaxAge = TimeSpan.FromSeconds(maxAgeSeconds) };

        var deadlines = SessionDeadlines.Of(Session("s1", SessionTestData.Start.UtcDateTime), limits, Options(o => o.IdleWarning = TimeSpan.FromSeconds(60)));

        Assert.Equal(warns, deadlines.WarnAt != null);
    }

    [Fact]
    public void NextWake_ShouldBeTheWarning_UntilItIsShown_ThenASecondPastIdle()
    {
        var t0 = SessionTestData.Start.UtcDateTime;
        var limits = new SessionLimits { IdleTimeout = Min(10), MaxAge = Min(60) };
        var deadlines = SessionDeadlines.Of(Session("s1", t0), limits, Options(o => o.IdleWarning = Min(2)));

        Assert.Equal(t0.AddMinutes(8), deadlines.NextWake(warned: false));
        Assert.Equal(t0.AddMinutes(10).AddSeconds(1), deadlines.NextWake(warned: true));
    }

    [Fact]
    public void NextWake_ShouldBeASecondPastMaxAge_WhenThatComesFirst()
    {
        var t0 = SessionTestData.Start.UtcDateTime;
        var limits = new SessionLimits { IdleTimeout = Min(10), MaxAge = Min(5) };
        var deadlines = SessionDeadlines.Of(Session("s1", t0), limits, Options(o => o.IdleWarning = Min(2)));

        Assert.Equal(t0.AddMinutes(5).AddSeconds(1), deadlines.NextWake(warned: false));
    }

    [Fact]
    public void NextWake_WithOnlyAMaxAge_ShouldBeASecondPastIt()
    {
        var t0 = SessionTestData.Start.UtcDateTime;

        var deadlines = SessionDeadlines.Of(Session("s1", t0), new SessionLimits { MaxAge = Min(30) }, Options());

        Assert.Equal(t0.AddMinutes(30).AddSeconds(1), deadlines.NextWake(warned: false));
    }
}
