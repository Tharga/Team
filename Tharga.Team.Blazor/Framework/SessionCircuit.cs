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
/// <b>Behaviour the tests hold it to.</b> Each wake reads the store afresh; checks never overlap (one in flight, at most one follow-up); a failure never ends the
/// circuit and is retried after <see cref="SessionOptions.ActivityInterval"/>; a hint for this person causes a check and nothing else; the warning needs a
/// view attached and is never shown to somebody whose store stamp is recent; <see cref="StayAsync"/> writes only while warned; an end is audited once and
/// stops the timer and the subscription, and signs the circuit's auth state out.
/// <para>
/// <b>No session id leaves it.</b> It learns the person from the session it reads, listens for that person only, and logs neither.
/// </para>
/// </remarks>
internal sealed class SessionCircuit(
    SessionEvaluator evaluator,
    AuthenticationStateProvider authentication,
    IHostEnvironmentAuthenticationStateProvider host,
    IUserSessionNotifier notifier,
    IOptions<ThargaBlazorOptions> options,
    TimeProvider timeProvider,
    ILogger<SessionCircuit> logger = null) : ISessionCircuit
{
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
    private string _userKey;
    private SessionDeadlines? _deadlines;
    private SessionCircuitState _state = SessionCircuitState.Watching;
    private DateTime? _idleAt;
    private string _endedLocation;

    public event Action Changed;

    public bool Active => _active;

    public SessionCircuitState State => _state;

    public DateTime? IdleAt => _state == SessionCircuitState.Warning ? _idleAt : null;

    public TimeSpan? Remaining => IdleAt is { } idleAt ? Max(idleAt - Now, TimeSpan.Zero) : null;

    public string EndedLocation => _endedLocation;

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task StartAsync()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        try
        {
            if (!evaluator.Active || authentication == null) return;

            authentication.AuthenticationStateChanged += OnAuthenticationStateChanged;
            if (UserSessionService.SessionIdOf(await PrincipalAsync()) == null)
            {
                authentication.AuthenticationStateChanged -= OnAuthenticationStateChanged;
                return;
            }

            lock (_gate)
            {
                if (_disposed) return;

                _active = true;
                _timer = timeProvider.CreateTimer(_ => CheckAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            notifier.SessionEnded += OnSessionEnded;
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

    public async Task StayAsync()
    {
        lock (_gate)
        {
            if (_state != SessionCircuitState.Warning || _ended || _disposed) return;
        }

        try
        {
            var principal = await PrincipalAsync();
            await evaluator.ConfirmPresenceAsync(principal, TeamOf(principal));
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
            ended = await evaluator.SignOutAsync(await PrincipalAsync());
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

        _ = RunChecksAsync(done);
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
        }

        Unsubscribe();
    }

    private async Task RunChecksAsync(TaskCompletionSource done)
    {
        try
        {
            while (true)
            {
                await LookAsync();

                lock (_gate)
                {
                    if (!_again || _ended || _disposed) break;

                    _again = false;
                }
            }
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The live session check failed unexpectedly; the circuit carries on.");
        }
        finally
        {
            lock (_gate)
            {
                _checking = false;
            }

            done.TrySetResult();
        }
    }

    private async Task LookAsync()
    {
        SessionCheck check;
        try
        {
            var principal = await PrincipalAsync();
            check = await evaluator.InspectCircuitAsync(principal, TeamOf(principal));
        }
        catch (Exception e)
        {
            // Never an end: a database blip must not sign anybody out.
            logger?.LogWarning(e, "The live session check failed; the circuit carries on and looks again in {Interval}.", _options.ActivityInterval);
            Arm(_options.ActivityInterval);
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
                // Not there, or not the caller's: the request check refuses it; the circuit only looks again.
                Arm(_options.ActivityInterval);
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

            var warn = due && _views > 0;
            changed = warn != (_state == SessionCircuitState.Warning);
            _state = warn ? SessionCircuitState.Warning : SessionCircuitState.Watching;
            _idleAt = warn ? deadlines.IdleAt : null;

            ArmAt(deadlines.NextWake(due), now);
        }

        if (changed) RaiseChanged();
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
        }

        Unsubscribe();

        try
        {
            host?.SetAuthenticationState(Task.FromResult(SignedOutState));
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The ended session's circuit could not be signed out; the next request is refused regardless.");
        }

        RaiseChanged();
    }

    private void Detach()
    {
        bool changed;
        lock (_gate)
        {
            if (_views > 0) _views--;

            // Nobody left to ask: the session still ends on time.
            changed = _views == 0 && _state == SessionCircuitState.Warning;
            if (changed)
            {
                _state = SessionCircuitState.Watching;
                _idleAt = null;
            }
        }

        if (changed) RaiseChanged();
    }

    private void OnSessionEnded(object sender, UserSessionEndedEventArgs e)
    {
        if (e?.UserKey == null) return;

        var userKey = _userKey;
        if (userKey != null && !string.Equals(userKey, e.UserKey, StringComparison.Ordinal)) return;

        _ = CheckAsync();
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task) => _authenticationState = task;

    private async Task<ClaimsPrincipal> PrincipalAsync() => (await (_authenticationState ?? authentication.GetAuthenticationStateAsync())).User;

    private void Unsubscribe()
    {
        notifier.SessionEnded -= OnSessionEnded;
        if (authentication != null) authentication.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }

    private void Arm(TimeSpan due)
    {
        lock (_gate)
        {
            if (_ended || _disposed) return;

            _timer?.Change(Max(due, TimeSpan.Zero), Timeout.InfiniteTimeSpan);
        }
    }

    private void ArmAt(DateTime? at, DateTime now)
    {
        if (at is { } moment) _timer?.Change(Max(moment - now, TimeSpan.Zero), Timeout.InfiniteTimeSpan);
        else Disarm();
    }

    private void Disarm()
    {
        lock (_gate)
        {
            if (_ended || _disposed) return;

            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception e)
            {
                logger?.LogWarning(e, "A session watch view threw when told the state changed.");
            }
        }
    }

    private static string TeamOf(ClaimsPrincipal principal) => principal?.FindFirst(TeamClaimTypes.SelectedTeamKey)?.Value;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private sealed class Attachment(SessionCircuit circuit) : IDisposable
    {
        private int _detached;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _detached, 1) == 0) circuit.Detach();
        }
    }
}
