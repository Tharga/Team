namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Settings for server-side sessions (Tharga/Team#471). Nothing here matters until an <see cref="ISessionPolicy"/> is registered; with none, sign-in, every
/// request and sign-out work exactly as they did before sessions existed.
/// </summary>
public sealed class SessionOptions
{
    /// <summary>
    /// Whether a registered policy is applied. Default <c>true</c>. Set it to <c>false</c> to switch sessions off without removing the policy: nothing is then
    /// read, written or audited, and the startup checks do not apply.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long a session read from the store, and the policy's limits for it in a team, are remembered on this instance. Default 30 seconds.
    /// <see cref="TimeSpan.Zero"/> reads every time. A longer time is cheaper and means a session ended on another instance is noticed here later. A session
    /// is never ended as idle on a remembered stamp alone: the store is read again first.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often a session's activity is written, at most: by the request check, and by the page, which reports use of a visible tab because a Blazor
    /// circuit sends no request while someone works in it. Default 1 minute. The store throttles per session, so several tabs and instances cost no more.
    /// An idle timeout shorter than this cannot be told apart from activity.
    /// </summary>
    public TimeSpan ActivityInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long before an idle sign-out the person is asked "Still here?", by <c>SessionWatch</c>. Default 1 minute. <see cref="TimeSpan.Zero"/> asks nobody.
    /// Never longer than the idle timeout less twice <see cref="ActivityInterval"/>, so that somebody who answers always has time to be heard; a timeout too short
    /// for that gets no warning. Somebody who is working is never asked.
    /// </summary>
    public TimeSpan IdleWarning { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The warning that applies to <paramref name="idleTimeout"/>: <see cref="IdleWarning"/> clamped to the idle timeout less twice <see cref="ActivityInterval"/>; zero when there is none.</summary>
    internal TimeSpan EffectiveIdleWarning(TimeSpan idleTimeout)
    {
        var longest = idleTimeout - ActivityInterval - ActivityInterval;
        var warning = IdleWarning < longest ? IdleWarning : longest;

        return warning > TimeSpan.Zero ? warning : TimeSpan.Zero;
    }

    /// <summary>
    /// Where a browser whose session ended is sent when the request needs a sign-in, with the reason as a code in <c>?reason=</c> (<c>idle</c>,
    /// <c>maxage</c>, <c>newersignin</c>, <c>ended</c>, <c>signedout</c>). Null, the default, sends it to the start page. Only a path on this site is
    /// used; anything else falls back to the start page. The page should allow anonymous access, so the person can read it before signing in again.
    /// </summary>
    public string EndedPath { get; set; }

    /// <summary>
    /// Whether a cookie from before sessions existed is given a session on its next request (default), rather than being let through without one. It is
    /// never signed out: an upgrade signs nobody out. Its sign-in time is the cookie's <c>auth_time</c>, so turning a max age on can end older cookies at
    /// once, which is the point of a max age. With this off, such a cookie has no session and no limit applies to it until it expires.
    /// </summary>
    public bool AdoptExistingCookies { get; set; } = true;

    /// <summary>
    /// What happens to a request when the policy or the store fails. Default <see cref="SessionErrorBehavior.Allow"/>: a database blip must not sign
    /// everybody out. <see cref="SessionErrorBehavior.Deny"/> refuses the request instead, without ending the session, so the next one tries again. Either
    /// way the failure is logged. A live circuit is never ended for a failure, whatever this says.
    /// </summary>
    public SessionErrorBehavior OnError { get; set; } = SessionErrorBehavior.Allow;

    /// <summary>
    /// The cookie scheme the session check is chained into. Default <c>Cookies</c>, which is the one <c>AddThargaAuth</c> signs in with. A host that signs
    /// in by its own means with another cookie scheme names it here. Other schemes, such as API keys and MCP, are never touched.
    /// </summary>
    public string CookieScheme { get; set; } = "Cookies";
}

/// <summary>What happens to a request when the session policy or the session store fails.</summary>
public enum SessionErrorBehavior
{
    /// <summary>The request is refused, and the session is left as it is.</summary>
    Deny,

    /// <summary>The request is let through, as if no policy were registered. The default.</summary>
    Allow
}
