using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// One circuit's watch over its server-side session (Tharga/Team#471): one timer, armed to the next moment that matters, and the store as the only truth.
/// </summary>
/// <remarks>
/// <b>Behaviour the tests hold it to.</b> Each wake reads the store afresh; checks never overlap (one in flight, at most one follow-up, and a request that
/// arrives while one runs is never lost) and never run on the caller's thread; a failure never ends the circuit, and it and a session the store does not
/// have are looked at again after a doubling wait, from <see cref="SessionOptions.ActivityInterval"/> but never less than <see cref="ShortestRetryWait"/>, up
/// to <see cref="LongestRetryWait"/>; a hint for this person causes a check and nothing else; the warning needs a view attached and is never shown to
/// somebody whose store stamp is recent; <see cref="StayAsync"/> writes only while warned and before the idle moment; an end is audited once and stops the
/// timer and the subscription, and signs the circuit's auth state out.
/// <para>
/// <b>The timer is never armed further ahead than <see cref="LongestWait"/></b>, so a long max age is reached by looking again, not by a due time the
/// system timer refuses. It is never touched once disposed.
/// </para>
/// <para>
/// <b>No session id leaves it.</b> It learns the person from the session it reads, listens for that person only (and to nobody before it knows who that
/// is), and logs neither.
/// </para>
/// </remarks>
internal sealed class SessionCircuit(
    ICircuitSessionChecks checks,
    AuthenticationStateProvider authentication,
    IHostEnvironmentAuthenticationStateProvider host,
    IUserSessionNotifier notifier,
    IOptions<ThargaBlazorOptions> options,
    TimeProvider timeProvider,
    ILogger<SessionCircuit> logger = null) : ISessionCircuit
{
    /// <summary>The longest the timer is armed for; a later moment is reached by waking, looking and arming again.</summary>
    internal static readonly TimeSpan LongestWait = TimeSpan.FromDays(1);

    /// <summary>The longest wait before looking again after a failure, or for a session the store does not have; the revalidation interval is the backstop.</summary>
    internal static readonly TimeSpan LongestRetryWait = TimeSpan.FromMinutes(30);

    /// <summary>The shortest wait before looking again, whatever <see cref="SessionOptions.ActivityInterval"/> says, so a store that fails fast is never read in a loop.</summary>
    internal static readonly TimeSpan ShortestRetryWait = TimeSpan.FromSeconds(1);

    private static readonly AuthenticationState SignedOutState = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly object _gate = new();
    private readonly SessionOptions _options = options.Value.Sessions ?? new SessionOptions();
    private ITimer _timer;
    private Task<AuthenticationState> _authenticationState;
    private Task _inFlight = Task.CompletedTask;
    private bool _checking;
    private bool _again;
    private bool _started;
    private bool _active;
    private bool _ended;
    private bool _disposed;
    private int _views;
    private bool _asked;
    private TimeSpan _retryWait;
    private string _userKey;
    private SessionDeadlines? _deadlines;
    private SessionCircuitState _state = SessionCircuitState.Watching;
    private DateTime? _idleAt;
    private string _endedLocation;

    public event Action Changed;

    public event Action Activated;

    public bool Active => _active;

    public SessionCircuitState State => _state;

    public DateTime? IdleAt => _state == SessionCircuitState.Warning ? _idleAt : null;

    public TimeSpan? Remaining => IdleAt is { } idleAt ? Max(idleAt - Now, TimeSpan.Zero) : null;

    public string EndedLocation => _endedLocation;

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>The host's provider to sign out on an end: the registered one, else the framework's own provider, which implements it without registering it.</summary>
    private IHostEnvironmentAuthenticationStateProvider HostState => host ?? authentication as IHostEnvironmentAuthenticationStateProvider;

    public async Task StartAsync()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;

            _started = true;
        }

        try
        {
            if (!checks.Active || authentication == null) return;

            lock (_gate)
            {
                if (_disposed) return;

                authentication.AuthenticationStateChanged += OnAuthenticationStateChanged;
            }

            var principal = await PrincipalAsync();

            lock (_gate)
            {
                if (_disposed || UserSessionService.SessionIdOf(principal) == null)
                {
                    authentication.AuthenticationStateChanged -= OnAuthenticationStateChanged;
                    return;
                }

                _active = true;
                _timer = timeProvider.CreateTimer(_ => CheckAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                notifier.SessionEnded += OnSessionEnded;
            }

            Raise(Activated);
            await CheckAsync();
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The live session watch could not start; the session is still checked on each request.");
        }
    }

    public IDisposable Attach()
    {
        bool look;
        lock (_gate)
        {
            _views++;

            // A view that arrives inside the warning window is asked at once, on the store's word rather than the last one read.
            look = _active && !_ended && !_disposed && _deadlines?.WarnAt <= Now;
        }

        if (look) _ = CheckAsync();

        return new Attachment(this);
    }

    public IDisposable TryAttach()
    {
        lock (_gate)
        {
            if (_asked) return null;

            _asked = true;
        }

        return new Asker(this, Attach());
    }

    public async Task StayAsync()
    {
        lock (_gate)
        {
            if (_state != SessionCircuitState.Warning || _ended || _disposed || _idleAt <= Now) return;
        }

        try
        {
            var principal = await PrincipalAsync();
            await checks.ConfirmPresenceAsync(principal, TeamOf(principal));
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Could not record that the person is still there; the warning stands.");
            return;
        }

        await CheckAsync();
    }

    public async Task SignOutAsync()
    {
        lock (_gate)
        {
            if (!_active || _ended || _disposed) return;
        }

        bool ended;
        try
        {
            ended = await checks.SignOutAsync(await PrincipalAsync(), _userKey);
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Could not sign the session out from the live circuit; it carries on.");
            return;
        }

        // Another call ended it first: the store says why.
        if (ended) End(SessionEndReason.SignedOut);
        else await CheckAsync();
    }

    public Task CheckAsync()
    {
        TaskCompletionSource done;
        lock (_gate)
        {
            if (!_active || _ended || _disposed) return Task.CompletedTask;

            if (_checking)
            {
                _again = true;
                return _inFlight;
            }

            _checking = true;
            _again = false;
            done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = done.Task;
        }

        _ = Task.Run(() => RunChecksAsync(done));
        return done.Task;
    }

    /// <summary>The check in flight, or a completed task; for tests to await a timer's or a hint's work.</summary>
    internal Task SettledAsync()
    {
        lock (_gate)
        {
            return _inFlight;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _timer?.Dispose();
            Unsubscribe();
        }
    }

    private async Task RunChecksAsync(TaskCompletionSource done)
    {
        try
        {
            while (true)
            {
                await LookAsync();

                // Deciding to stop and saying so is one step: a request that arrives in between must find the check either running or stopped.
                lock (_gate)
                {
                    if (!_again || _ended || _disposed)
                    {
                        _checking = false;
                        break;
                    }

                    _again = false;
                }
            }
        }
        catch (Exception e)
        {
            lock (_gate)
            {
                _checking = false;
            }

            logger?.LogWarning(e, "The live session check failed unexpectedly; the circuit carries on.");
        }
        finally
        {
            done.TrySetResult();
        }
    }

    private async Task LookAsync()
    {
        SessionCheck check;
        try
        {
            var principal = await PrincipalAsync();
            check = await checks.InspectCircuitAsync(principal, TeamOf(principal));
        }
        catch (Exception e)
        {
            // Never an end: a database blip must not sign anybody out.
            var wait = NextRetryWait();
            logger?.LogWarning(e, "The live session check failed; the circuit carries on and looks again in {Interval}.", wait);
            Arm(wait);
            return;
        }

        switch (check.Outcome)
        {
            case SessionCheckOutcome.Ended:
                End(check.Reason ?? SessionEndReason.Ended);
                break;
            case SessionCheckOutcome.Valid when check.Session != null:
                Watch(check.Session, check.Limits);
                break;
            case SessionCheckOutcome.Valid:
                Disarm();
                break;
            default:
                // Not there, or not the caller's: the request check refuses it; the circuit only looks again, less and less often.
                Arm(NextRetryWait());
                break;
        }
    }

    private void Watch(UserSession session, SessionLimits limits)
    {
        var now = Now;
        var deadlines = SessionDeadlines.Of(session, limits, _options);
        var due = deadlines.WarnAt <= now;
        bool changed;

        lock (_gate)
        {
            if (_ended || _disposed) return;

            _userKey = session.UserKey;
            _deadlines = deadlines;
            _retryWait = TimeSpan.Zero;

            var warn = due && _views > 0;
            var idleAt = warn ? deadlines.IdleAt : null;
            changed = warn != (_state == SessionCircuitState.Warning) || (warn && idleAt != _idleAt);
            _state = warn ? SessionCircuitState.Warning : SessionCircuitState.Watching;
            _idleAt = idleAt;

            if (deadlines.NextWake(due) is { } wake) ChangeTimer(wake - now);
            else ChangeTimer(Timeout.InfiniteTimeSpan);
        }

        if (changed) Raise(Changed);
    }

    private void End(SessionEndReason reason)
    {
        lock (_gate)
        {
            if (_ended || _disposed) return;

            _ended = true;
            _state = SessionCircuitState.Ended;
            _idleAt = null;
            _endedLocation = SessionCookieEvents.EndedLocation(_options.EndedPath, ThargaAuthRegistration.HomePath, reason);
            _timer?.Dispose();
            Unsubscribe();
        }

        try
        {
            HostState?.SetAuthenticationState(Task.FromResult(SignedOutState));
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The ended session's circuit could not be signed out; the next request is refused regardless.");
        }

        Raise(Changed);
    }

    private void Detach()
    {
        bool changed;
        lock (_gate)
        {
            if (_views > 0) _views--;

            // Nobody left to ask: the session still ends on time, and another view may take over.
            changed = _views == 0 && !_ended && !_disposed;
            if (changed && _state == SessionCircuitState.Warning)
            {
                _state = SessionCircuitState.Watching;
                _idleAt = null;
            }
        }

        if (changed) Raise(Changed);
    }

    private void Unasked()
    {
        lock (_gate)
        {
            _asked = false;
        }
    }

    private TimeSpan NextRetryWait()
    {
        lock (_gate)
        {
            _retryWait = _retryWait <= TimeSpan.Zero ? Max(_options.ActivityInterval, ShortestRetryWait) : Min(_retryWait + _retryWait, LongestRetryWait);
            return _retryWait;
        }
    }

    private void OnSessionEnded(object sender, UserSessionEndedEventArgs e)
    {
        var userKey = _userKey;
        if (e?.UserKey == null || userKey == null || !string.Equals(userKey, e.UserKey, StringComparison.Ordinal)) return;

        _ = CheckAsync();
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task) => _authenticationState = task;

    private async Task<ClaimsPrincipal> PrincipalAsync() => (await (_authenticationState ?? authentication.GetAuthenticationStateAsync())).User;

    /// <summary>Called under the gate.</summary>
    private void Unsubscribe()
    {
        notifier.SessionEnded -= OnSessionEnded;
        if (authentication != null) authentication.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }

    private void Arm(TimeSpan due)
    {
        lock (_gate)
        {
            ChangeTimer(due);
        }
    }

    private void Disarm() => Arm(Timeout.InfiniteTimeSpan);

    /// <summary>Called under the gate; never touches a timer that is gone, and never arms further ahead than <see cref="LongestWait"/>.</summary>
    private void ChangeTimer(TimeSpan due)
    {
        if (_ended || _disposed || _timer == null) return;

        _timer.Change(due == Timeout.InfiniteTimeSpan ? due : Min(Max(due, TimeSpan.Zero), LongestWait), Timeout.InfiniteTimeSpan);
    }

    private void Raise(Action handlers)
    {
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception e)
            {
                logger?.LogWarning(e, "A session watch view threw when told the circuit changed.");
            }
        }
    }

    private static string TeamOf(ClaimsPrincipal principal) => principal?.FindFirst(TeamClaimTypes.SelectedTeamKey)?.Value;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private sealed class Attachment(SessionCircuit circuit) : IDisposable
    {
        private int _detached;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _detached, 1) == 0) circuit.Detach();
        }
    }

    private sealed class Asker(SessionCircuit circuit, IDisposable attachment) : IDisposable
    {
        private int _gone;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _gone, 1) != 0) return;

            circuit.Unasked();
            attachment.Dispose();
        }
    }
}
