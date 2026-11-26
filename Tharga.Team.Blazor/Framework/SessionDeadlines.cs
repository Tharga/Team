using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// The moments that matter to one session, worked out from what the store says and the host's limits; pure, so a circuit's timer can be tested without one.
/// All times are UTC and the server's own: nothing here is ever taken from a browser.
/// </summary>
/// <param name="WarnAt">When "Still here?" is due; null when there is no idle limit, no warning, or max age ends the session no later than the idle limit would.</param>
/// <param name="IdleAt">When the session is idle for longer than allowed; null when there is no idle limit.</param>
/// <param name="MaxAgeAt">When the session is older than allowed; null when there is no max age.</param>
internal readonly record struct SessionDeadlines(DateTime? WarnAt, DateTime? IdleAt, DateTime? MaxAgeAt)
{
    /// <summary>How long after a deadline the session is looked at, so it is over the limit (the limits are inclusive) when it is.</summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromSeconds(1);

    /// <summary>The deadlines of <paramref name="session"/> under <paramref name="limits"/>.</summary>
    public static SessionDeadlines Of(UserSession session, SessionLimits limits, SessionOptions options)
    {
        if (session == null || limits == null) return default;

        var idleAt = limits.IdleTimeout is { } idle ? session.LastActivityAt + idle : (DateTime?)null;
        var maxAgeAt = limits.MaxAge is { } maxAge ? session.SignedInAt + maxAge : (DateTime?)null;
        var warning = limits.IdleTimeout is { } timeout ? (options ?? new SessionOptions()).EffectiveIdleWarning(timeout) : TimeSpan.Zero;

        // Staying would not help when max age ends the session first, so nobody is asked.
        var warns = idleAt != null && warning > TimeSpan.Zero && (maxAgeAt == null || maxAgeAt > idleAt);

        return new SessionDeadlines(warns ? idleAt - warning : null, idleAt, maxAgeAt);
    }

    /// <summary>The next moment to look at the session: the warning (unless it is already shown), else a second past idle or max age, whichever is first; null when nothing is due.</summary>
    public DateTime? NextWake(bool warned) => Earliest(warned ? null : WarnAt, IdleAt + Margin, MaxAgeAt + Margin);

    private static DateTime? Earliest(params DateTime?[] moments)
    {
        DateTime? earliest = null;
        foreach (var moment in moments)
        {
            if (moment != null && (earliest == null || moment < earliest)) earliest = moment;
        }

        return earliest;
    }
}
