using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Framework;

/// <summary>What the session check decided for one request.</summary>
internal enum SessionCheckOutcome
{
    /// <summary>The session is fine, or sessions are not active; nothing changes.</summary>
    Valid,

    /// <summary>A cookie with no session was given one; <see cref="SessionCheck.Principal"/> is the principal carrying it.</summary>
    Adopted,

    /// <summary>The request is refused although no session of this caller was ended: the claim names a session that is not there, or is another person's, or the check failed and <see cref="SessionErrorBehavior.Deny"/> is set. Nothing is audited as an end.</summary>
    Rejected,

    /// <summary>The session is ended; <see cref="SessionCheck.Reason"/> says why. Audited already, once.</summary>
    Ended
}

/// <summary>What the session check decided, with the principal to carry on with when it adopted a cookie, and why when the session is ended.</summary>
/// <param name="Outcome">What was decided.</param>
/// <param name="Principal">The principal to carry on with when a cookie was adopted.</param>
/// <param name="Reason">Why the session ended, when it did.</param>
/// <param name="Session">The session as read, when the check read one; what a live circuit works out its deadlines from.</param>
/// <param name="Limits">The host's limits for the session's team, when the check looked them up.</param>
internal sealed record SessionCheck(SessionCheckOutcome Outcome, ClaimsPrincipal Principal = null, SessionEndReason? Reason = null, UserSession Session = null, SessionLimits Limits = null);

/// <summary>
/// Checks the caller's server-side session against the host's <see cref="ISessionPolicy"/>, per request and per live circuit (Tharga/Team#471). Shaped like
/// <see cref="TeamEntryEvaluator"/>.
/// </summary>
/// <remarks>
/// <b>Nothing registered, or switched off, is the old behaviour exactly:</b> valid, with no lookup and no audit. Otherwise, in order: a cookie with no
/// session is adopted; the session is read (remembered per instance for <see cref="SessionOptions.CacheDuration"/>) and must be the caller's own; the
/// policy's limits for the team are applied; a single-session team is entered exclusively; activity is recorded at most once per
/// <see cref="SessionOptions.ActivityInterval"/>. A policy or store that fails is handled as <see cref="SessionOptions.OnError"/> says.
/// <para>
/// <b>It holds no store.</b> Every read and write goes through <see cref="UserSessionService"/>, which keys each one by the caller's own principal, checks
/// that the session is theirs, and audits an end exactly once; this class decides only when. The host's numbers become an end in
/// <see cref="UserSessionService.Exceeded"/>.
/// </para>
/// <para>
/// <b>An idle session is never ended on a remembered stamp.</b> Activity is recorded by other instances, and by the page itself for someone working inside
/// a circuit, so the store is read again before an idle end, and its stamp decides.
/// </para>
/// </remarks>
internal sealed class SessionEvaluator(
    SessionCache cache,
    IOptions<ThargaBlazorOptions> options,
    TimeProvider timeProvider,
    ISessionPolicy policy = null,
    UserSessionService sessions = null,
    ILogger<SessionEvaluator> logger = null)
{
    private static readonly SessionCheck ValidCheck = new(SessionCheckOutcome.Valid);
    private static readonly SessionCheck RejectedCheck = new(SessionCheckOutcome.Rejected);

    private readonly SessionOptions _options = options.Value.Sessions ?? new SessionOptions();

    /// <summary>Whether a policy is registered and sessions are switched on, which is the only case in which anything here costs anything.</summary>
    public bool Active => policy != null && _options.Enabled;

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // Null only when a policy is registered with no store, which the startup check refuses; a request that gets here anyway is a failure, handled as one.
    private UserSessionService Sessions => sessions ?? throw new InvalidOperationException($"Server-side sessions are on, but no {nameof(IUserSessionStore)} is registered to keep them in.");

    /// <summary>Checks the session of a request, as the cookie holds it; adopts a cookie with no session, enters a single-session team, and records activity.</summary>
    /// <param name="principal">The caller as the cookie holds them.</param>
    /// <param name="issuedUtc">When the cookie was issued, used as sign-in time when an adopted cookie has no <c>auth_time</c>.</param>
    /// <param name="activeTeamKey">The team named by the selected-team cookie, or null.</param>
    /// <param name="cancellationToken">The request's.</param>
    public Task<SessionCheck> EvaluateAsync(ClaimsPrincipal principal, DateTimeOffset? issuedUtc, string activeTeamKey, CancellationToken cancellationToken = default)
        => CheckAsync(principal, issuedUtc, activeTeamKey, true, cancellationToken);

    /// <summary>
    /// Checks the session of a live circuit, which sends no request: it ends a session over its limits, and does nothing else. It never adopts (that needs
    /// the cookie), never enters a team (the page load did) and never records activity, because the revalidation loop runs for an idle tab too.
    /// </summary>
    public Task<SessionCheck> EvaluateCircuitAsync(ClaimsPrincipal principal, string activeTeamKey, CancellationToken cancellationToken = default)
        => UserSessionService.SessionIdOf(principal) == null ? Task.FromResult(ValidCheck) : CheckAsync(principal, null, activeTeamKey, false, cancellationToken);

    /// <summary>
    /// Looks at the session of a live circuit for its own timer: always read fresh from the store (never the remembered one), ends it when over its limits, and
    /// returns the session and limits so deadlines can be worked out. It never adopts, never records activity and never enters a team.
    /// </summary>
    /// <remarks>A failing store or policy propagates, unlike <see cref="EvaluateCircuitAsync"/>: the circuit must tell a failure from a refusal, and never ends for a failure.</remarks>
    public async Task<SessionCheck> InspectCircuitAsync(ClaimsPrincipal principal, string activeTeamKey, CancellationToken cancellationToken = default)
    {
        if (!Active || UserSessionService.SessionIdOf(principal) == null) return ValidCheck;

        var session = await Sessions.GetAsync(principal, cancellationToken);
        if (session == null) return RejectedCheck;
        if (session.EndedAt != null) return EndedCheck(session.EndReason) with { Session = session };

        var limits = await LimitsAsync(session, TeamOf(activeTeamKey), cancellationToken);
        if (UserSessionService.Exceeded(session, limits, Now) is { } reason) return await EndAsync(principal, session, reason, cancellationToken) with { Session = session };

        return new SessionCheck(SessionCheckOutcome.Valid, Session: session, Limits: limits);
    }

    /// <summary>The person answered "Still here?": records activity now, whatever was recorded a moment ago, and forgets the remembered session. False when the session is not there or has ended.</summary>
    public async Task<bool> ConfirmPresenceAsync(ClaimsPrincipal principal, string activeTeamKey, CancellationToken cancellationToken = default)
    {
        var sessionId = UserSessionService.SessionIdOf(principal);
        if (!Active || sessionId == null) return false;

        var recorded = await Sessions.RecordActivityAsync(principal, TeamOf(activeTeamKey), TimeSpan.Zero, cancellationToken);
        cache.ForgetSession(sessionId);

        return recorded;
    }

    /// <summary>The person chose to sign out from a live circuit: ends the session as <see cref="SessionEndReason.SignedOut"/>, audited and announced once; true when this call ended it.</summary>
    public async Task<bool> SignOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var sessionId = UserSessionService.SessionIdOf(principal);
        if (!Active || sessionId == null) return false;

        var ended = await Sessions.SignOutAsync(principal, cancellationToken);
        cache.ForgetSession(sessionId);

        return ended;
    }

    /// <summary>Records that the caller used a visible page, for the activity endpoint; the store throttles it to once per <see cref="SessionOptions.ActivityInterval"/>.</summary>
    public async Task<bool> RecordActivityAsync(ClaimsPrincipal principal, string activeTeamKey, CancellationToken cancellationToken = default)
    {
        if (!Active || sessions == null || UserSessionService.SessionIdOf(principal) == null) return false;

        return await sessions.RecordActivityAsync(principal, TeamOf(activeTeamKey), _options.ActivityInterval, cancellationToken);
    }

    private async Task<SessionCheck> CheckAsync(ClaimsPrincipal principal, DateTimeOffset? issuedUtc, string activeTeamKey, bool request, CancellationToken cancellationToken)
    {
        if (!Active) return ValidCheck;

        var team = TeamOf(activeTeamKey);
        if (UserSessionService.SessionIdOf(principal) != null) return await GuardAsync(() => CheckSessionAsync(principal, team, request, cancellationToken));
        if (!request || !_options.AdoptExistingCookies || principal?.Identity?.IsAuthenticated != true) return ValidCheck;

        var adoption = await GuardAsync(() => AdoptAsync(principal, issuedUtc, cancellationToken));
        if (adoption.Outcome != SessionCheckOutcome.Adopted) return adoption;

        // The adopted session is checked like any other: its age counts from the original sign-in, so a max age can end it at once.
        var check = await GuardAsync(() => CheckSessionAsync(adoption.Principal, team, request, cancellationToken));
        return check.Outcome == SessionCheckOutcome.Valid ? adoption : check;
    }

    private async Task<SessionCheck> AdoptAsync(ClaimsPrincipal principal, DateTimeOffset? issuedUtc, CancellationToken cancellationToken)
    {
        var (adopted, session) = await Sessions.AdoptAsync(principal, issuedUtc, cancellationToken);
        cache.SetSession(session, _options.CacheDuration);

        return new SessionCheck(SessionCheckOutcome.Adopted, adopted);
    }

    private async Task<SessionCheck> CheckSessionAsync(ClaimsPrincipal principal, string team, bool request, CancellationToken cancellationToken)
    {
        var session = await ReadAsync(principal, cancellationToken);
        if (session == null) return RejectedCheck;
        if (session.EndedAt != null) return EndedCheck(session.EndReason);

        var limits = await LimitsAsync(session, team, cancellationToken);
        var now = Now;
        var exceeded = UserSessionService.Exceeded(session, limits, now);

        if (exceeded == SessionEndReason.Idle)
        {
            session = await ReadAgainAsync(principal, cancellationToken);
            if (session == null) return RejectedCheck;
            if (session.EndedAt != null) return EndedCheck(session.EndReason);

            exceeded = UserSessionService.Exceeded(session, limits, now);
        }

        if (exceeded is { } reason) return await EndAsync(principal, session, reason, cancellationToken);

        if (request && limits.SingleSession && team != null && !string.Equals(session.ActiveTeamKey, team, StringComparison.Ordinal))
        {
            var entry = await Sessions.EnterTeamExclusiveAsync(principal, team, cancellationToken);

            // Read afresh next time: the store now says which team it is in, and whether a racing entry elsewhere ended it.
            cache.ForgetSession(session.SessionId);
            if (entry.CallerEnded) return EndedCheck(SessionEndReason.NewerSignIn);
        }

        if (request && now - session.LastActivityAt >= _options.ActivityInterval && await Sessions.RecordActivityAsync(principal, team, _options.ActivityInterval, cancellationToken))
        {
            cache.UpdateSession(session with { LastActivityAt = now, ActiveTeamKey = team });
        }

        return ValidCheck;
    }

    private async Task<UserSession> ReadAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (cache.TryGetSession(UserSessionService.SessionIdOf(principal), out var cached)) return Sessions.Owns(principal, cached) ? cached : null;

        var session = await Sessions.GetAsync(principal, cancellationToken);
        cache.SetSession(session, _options.CacheDuration);

        return session;
    }

    private async Task<UserSession> ReadAgainAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        cache.ForgetSession(UserSessionService.SessionIdOf(principal));
        return await ReadAsync(principal, cancellationToken);
    }

    private async Task<SessionLimits> LimitsAsync(UserSession session, string team, CancellationToken cancellationToken)
    {
        if (cache.TryGetLimits(session.SessionId, team, out var cached)) return cached;

        var limits = await policy.GetLimitsAsync(session, team, cancellationToken) ?? SessionLimits.None;
        cache.SetLimits(session.SessionId, team, limits, _options.CacheDuration);

        return limits;
    }

    /// <remarks>Only the call the store says ended it is audited, by the domain service; another that lost the race reports the reason the store kept.</remarks>
    private async Task<SessionCheck> EndAsync(ClaimsPrincipal principal, UserSession session, SessionEndReason reason, CancellationToken cancellationToken)
    {
        if (await Sessions.EndAsync(principal, reason, cancellationToken))
        {
            cache.UpdateSession(session with { EndedAt = Now, EndReason = reason });
            return EndedCheck(reason);
        }

        var stored = await ReadAgainAsync(principal, cancellationToken);
        return stored == null ? RejectedCheck : EndedCheck(stored.EndReason ?? reason);
    }

    /// <summary>A failing policy or store, handled as configured; a cancelled request is not a failure and is not swallowed.</summary>
    private async Task<SessionCheck> GuardAsync(Func<Task<SessionCheck>> check)
    {
        try
        {
            return await check();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The session id is a key and stays out of the log.
            logger?.LogWarning(e, "The server-side session check failed; the request is {Behavior} as configured.", _options.OnError == SessionErrorBehavior.Allow ? "allowed" : "refused");

            return _options.OnError == SessionErrorBehavior.Allow ? ValidCheck : RejectedCheck;
        }
    }

    private static SessionCheck EndedCheck(SessionEndReason? reason) => new(SessionCheckOutcome.Ended, Reason: reason ?? SessionEndReason.Ended);

    private static string TeamOf(string teamKey) => string.IsNullOrWhiteSpace(teamKey) ? null : teamKey;
}
