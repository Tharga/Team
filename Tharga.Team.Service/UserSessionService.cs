using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service;

/// <summary>
/// The domain service for server-side sessions (Tharga/Team#471): the one place a session is started, read, ended, marked active or made the only one in
/// a team, and the one place those are audited.
/// </summary>
/// <remarks>
/// <b>Every operation is keyed by the caller's own principal</b>, and the session it names is the one the library stamped into the data-protected cookie.
/// Nothing here takes a session id or a user from its caller, so nothing here can reach someone else's session; a read additionally checks that the
/// session's subject is the principal's, as defence in depth. That is why the Blazor layer's cookie check, activity endpoint and circuit revalidation reach
/// sessions through this rather than through the unchecked <see cref="IUserSessionStore"/>: the rule lives here, once.
/// <para>
/// <b>The public half is <see cref="IUserSessionService"/></b>, which a host may call or replace. The internal half is the per-request check's, which the
/// library keeps for itself: a host replacing the interface does not replace the check.
/// </para>
/// <para>
/// <b>An end is audited by the call that ended it</b>, which the store tells exactly one caller, so two instances refusing the same request at once leave
/// one entry. An audit that fails never fails the operation it records.
/// </para>
/// </remarks>
internal sealed class UserSessionService(
    IUserSessionStore store,
    IUserService userService,
    ITeamPrincipalAccessor principalAccessor,
    TimeProvider timeProvider,
    UserIdentityResolver identityResolver = null,
    CompositeAuditLogger auditLogger = null,
    ILogger<UserSessionService> logger = null,
    IUserSessionNotifier notifier = null) : IUserSessionService
{
    /// <summary>The issuer of the session claim: the library, never the identity provider.</summary>
    internal const string ClaimIssuer = "Tharga.Team";

    private const int IdBytes = 32;
    private const string AuthTimeClaimType = "auth_time";
    private const string IdentityProviderClaimType = "idp";
    private const string AuthenticationMethodsClaimType = "amr";

    // The names the inbound claim map gives the same three claims, for a host that leaves the map on.
    private const string MappedIdentityProviderClaimType = "http://schemas.microsoft.com/identity/claims/identityprovider";
    private const string MappedAuthenticationMethodsClaimType = "http://schemas.microsoft.com/claims/authnmethodsreferences";

    private UserIdentityResolver Resolver => identityResolver ?? UserIdentityResolver.Default;

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public Task<ClaimsPrincipal> StartAsync(ClaimsPrincipal principal) => StartAsync(principal, SessionOrigin.Host);

    public async Task<bool> EndCurrentAsync()
    {
        var principal = await principalAccessor.GetCurrentAsync();
        return await SignOutAsync(principal);
    }

    public async Task<int> EndOtherSessionsAsync()
    {
        var principal = await principalAccessor.GetCurrentAsync();
        var sessionId = SessionIdOf(principal);
        if (sessionId == null) return 0;

        var user = await userService.GetCurrentUserAsync(principal, recordActivity: false);
        if (string.IsNullOrEmpty(user?.Key)) return 0;

        var ended = await store.EndOthersAsync(user.Key, sessionId, Now);
        if (ended > 0) Announce(user.Key, SessionEndReason.Ended);

        return ended;
    }

    /// <summary>
    /// Ends the given principal's session as <see cref="SessionEndReason.SignedOut"/>, audited and announced once; true when this call ended it.
    /// <see cref="EndCurrentAsync"/> is this for the ambient caller. Pass <paramref name="userKey"/> when holding the session, so the end is announced without a user lookup.
    /// </summary>
    internal async Task<bool> SignOutAsync(ClaimsPrincipal principal, string userKey = null, CancellationToken cancellationToken = default)
    {
        var sessionId = SessionIdOf(principal);
        if (sessionId == null) return false;

        var ended = await store.EndAsync(sessionId, SessionEndReason.SignedOut, Now, cancellationToken);
        if (!ended) return false;

        Audit(() => AuthAuditEntries.SignOut(principal, Resolver));
        await AnnounceAsync(principal, userKey, SessionEndReason.SignedOut);

        return true;
    }

    /// <summary>Starts a session with the given origin and returns the principal carrying its claim; the library's own sign-in starts <see cref="SessionOrigin.SignIn"/>.</summary>
    internal async Task<ClaimsPrincipal> StartAsync(ClaimsPrincipal principal, SessionOrigin origin, CancellationToken cancellationToken = default)
        => (await StartSessionAsync(principal, origin, null, cancellationToken)).Principal;

    /// <summary>
    /// Gives a cookie from before sessions existed a session of its own (<see cref="SessionOrigin.Adopted"/>), rather than signing it out. Its sign-in time is
    /// the cookie's <c>auth_time</c>, else when the cookie was issued, else now, so a max age counts from the original sign-in.
    /// </summary>
    internal Task<(ClaimsPrincipal Principal, UserSession Session)> AdoptAsync(ClaimsPrincipal principal, DateTimeOffset? issuedUtc, CancellationToken cancellationToken = default)
        => StartSessionAsync(principal, SessionOrigin.Adopted, issuedUtc, cancellationToken);

    /// <summary>The session the principal's claim names, when it is the principal's own; null when there is no claim, no such session, or it is someone else's.</summary>
    internal async Task<UserSession> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var sessionId = SessionIdOf(principal);
        if (sessionId == null) return null;

        var session = await store.GetAsync(sessionId, cancellationToken);
        return Owns(principal, session) ? session : null;
    }

    /// <summary>Whether <paramref name="session"/> is the one the principal's claim names and was started for the principal's subject.</summary>
    internal bool Owns(ClaimsPrincipal principal, UserSession session)
        => session != null
           && string.Equals(session.SessionId, SessionIdOf(principal), StringComparison.Ordinal)
           && string.Equals(session.Subject, Resolver.GetSubject(principal), StringComparison.Ordinal);

    /// <summary>Ends the principal's session for <paramref name="reason"/>; true, and audited, when this call ended it. Pass <paramref name="userKey"/> when holding the session.</summary>
    internal async Task<bool> EndAsync(ClaimsPrincipal principal, SessionEndReason reason, string userKey = null, CancellationToken cancellationToken = default)
    {
        var sessionId = SessionIdOf(principal);
        if (sessionId == null) return false;

        var ended = await store.EndAsync(sessionId, reason, Now, cancellationToken);
        if (!ended) return false;

        Audit(() => AuthAuditEntries.SessionEnded(principal, reason, Resolver));
        await AnnounceAsync(principal, userKey, reason);

        return true;
    }

    /// <summary>Records that the principal used their session now, in <paramref name="teamKey"/>, unless it was recorded less than <paramref name="minInterval"/> ago.</summary>
    internal Task<bool> RecordActivityAsync(ClaimsPrincipal principal, string teamKey, TimeSpan minInterval, CancellationToken cancellationToken = default)
    {
        var sessionId = SessionIdOf(principal);
        return sessionId == null ? Task.FromResult(false) : store.RecordActivityAsync(sessionId, Now, teamKey, minInterval, cancellationToken);
    }

    /// <summary>
    /// Makes the principal's session the only one of theirs in <paramref name="teamKey"/>, newest sign-in winning; audits each older session it ended, and the
    /// principal's own when a newer one was there first. One notification tells the host when anything was ended, however many sessions it was. Pass
    /// <paramref name="userKey"/> when holding the session.
    /// </summary>
    internal async Task<SessionTeamEntry> EnterTeamExclusiveAsync(ClaimsPrincipal principal, string teamKey, string userKey = null, CancellationToken cancellationToken = default)
    {
        var sessionId = SessionIdOf(principal);
        if (sessionId == null) return new SessionTeamEntry([], false);

        var entry = await store.EnterTeamExclusiveAsync(sessionId, teamKey, Now, cancellationToken);

        foreach (var superseded in entry.Superseded ?? [])
        {
            Audit(() => AuthAuditEntries.SessionSuperseded(superseded));
        }

        if (entry.CallerEnded) Audit(() => AuthAuditEntries.SessionEnded(principal, SessionEndReason.NewerSignIn, Resolver));

        if (entry.CallerEnded || entry.Superseded is { Length: > 0 }) await AnnounceAsync(principal, userKey, SessionEndReason.NewerSignIn);

        return entry;
    }

    /// <summary>The limit <paramref name="session"/> is over at <paramref name="now"/>, or null. The one place the host's numbers are turned into an end.</summary>
    /// <remarks>A max age counts from the sign-in, however active the session; an idle timeout from the last recorded activity. Reaching a limit exactly is still within it.</remarks>
    internal static SessionEndReason? Exceeded(UserSession session, SessionLimits limits, DateTime now)
    {
        if (session == null || limits == null) return null;
        if (limits.MaxAge is { } maxAge && now - session.SignedInAt > maxAge) return SessionEndReason.MaxAge;
        if (limits.IdleTimeout is { } idle && now - session.LastActivityAt > idle) return SessionEndReason.Idle;

        return null;
    }

    /// <summary>The session id the principal carries, or null.</summary>
    internal static string SessionIdOf(ClaimsPrincipal principal)
    {
        var value = principal?.FindFirst(TeamClaimTypes.SessionId)?.Value;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>A copy of <paramref name="principal"/> with no session claim on any identity: one that did not come from the library is never believed.</summary>
    internal static ClaimsPrincipal WithoutSessionClaim(ClaimsPrincipal principal)
    {
        if (principal == null) return null;

        var copy = principal.Clone();
        foreach (var identity in copy.Identities)
        {
            foreach (var claim in identity.FindAll(TeamClaimTypes.SessionId).ToArray())
            {
                identity.RemoveClaim(claim);
            }
        }

        return copy;
    }

    private async Task<(ClaimsPrincipal Principal, UserSession Session)> StartSessionAsync(ClaimsPrincipal principal, SessionOrigin origin, DateTimeOffset? issuedUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var user = await userService.GetCurrentUserAsync(principal, recordActivity: false);
        if (string.IsNullOrEmpty(user?.Key)) throw new InvalidOperationException("No user could be resolved for the principal, so no session can be started for it.");

        var now = Now;
        string[] methods = [.. principal.FindAll(AuthenticationMethodsClaimType).Concat(principal.FindAll(MappedAuthenticationMethodsClaimType)).Select(x => x.Value).Distinct(StringComparer.Ordinal)];
        var session = new UserSession
        {
            SessionId = NewSessionId(),
            UserKey = user.Key,
            Subject = Resolver.GetSubject(principal),
            SignedInAt = AuthTime(principal) ?? issuedUtc?.UtcDateTime ?? now,
            IdentityProvider = (principal.FindFirst(IdentityProviderClaimType) ?? principal.FindFirst(MappedIdentityProviderClaimType))?.Value,
            AuthenticationMethods = methods.Length == 0 ? null : methods,
            Origin = origin,
            LastActivityAt = now
        };

        await store.StartAsync(session, cancellationToken);

        return (Stamped(principal, session.SessionId), session);
    }

    private static ClaimsPrincipal Stamped(ClaimsPrincipal principal, string sessionId)
    {
        var stamped = WithoutSessionClaim(principal);
        if (stamped.Identity is not ClaimsIdentity identity)
        {
            identity = new ClaimsIdentity();
            stamped.AddIdentity(identity);
        }

        identity.AddClaim(new Claim(TeamClaimTypes.SessionId, sessionId, ClaimValueTypes.String, ClaimIssuer));
        return stamped;
    }

    /// <summary>256 random bits, base64url: the session's only key, so it must not be guessable.</summary>
    private static string NewSessionId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(IdBytes));

    private static DateTime? AuthTime(ClaimsPrincipal principal)
    {
        var value = (principal.FindFirst(AuthTimeClaimType) ?? principal.FindFirst(ClaimTypes.AuthenticationInstant))?.Value;
        if (string.IsNullOrEmpty(value)) return null;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.UtcDateTime : null;
    }

    /// <summary>
    /// Tells the host whose session ended, after the store and the audit, so whoever looks finds it ended. The person is <paramref name="userKey"/> when the
    /// caller held the session, and looked up only when it did not. The end stands whatever happens here.
    /// </summary>
    private async Task AnnounceAsync(ClaimsPrincipal principal, string userKey, SessionEndReason reason)
    {
        if (notifier == null) return;

        if (!string.IsNullOrEmpty(userKey))
        {
            Announce(userKey, reason);
            return;
        }

        try
        {
            var user = await userService.GetCurrentUserAsync(principal, recordActivity: false);
            Announce(user?.Key, reason);
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The end of a server-side session could not be announced.");
        }
    }

    private void Announce(string userKey, SessionEndReason reason)
    {
        if (notifier == null || string.IsNullOrEmpty(userKey)) return;

        try
        {
            notifier.Notify(new UserSessionEndedEventArgs { UserKey = userKey, Reason = reason });
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The end of a server-side session could not be announced.");
        }
    }

    private void Audit(Func<AuditEntry> entry)
    {
        if (auditLogger == null) return;

        try
        {
            auditLogger.Log(entry());
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "A server-side session event could not be audited.");
        }
    }
}
