using System.Collections.Concurrent;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Counts failed invite-code resolves per source inside a sliding window, and says how long the next
/// failure should be delayed.
/// </summary>
/// <remarks>
/// <b>Deliberately process-local, which is a limitation rather than an oversight.</b> Spread across
/// instances an attacker gets one budget per instance. That is the right trade here because the alternative
/// is a third host-implemented port for shared state: <see cref="ITeamCache"/> is purpose-built for three
/// named claims lookups and adding to it would break every host that implemented it, and
/// <c>ISupportEventLedger</c> records an event once rather than counting within a window. A per-process
/// delay still slows every connection an attacker holds, and — unlike a refusal budget — being wrong about
/// the count can never lock anybody out.
/// <para>
/// <b>Only failures are counted.</b> A resolve that finds an invitation is somebody holding a real link;
/// counting it would throttle the success path an invitation exists to serve.
/// </para>
/// </remarks>
internal sealed class InvitationThrottle(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, Attempts> _bySource = new(StringComparer.Ordinal);

    /// <summary>
    /// Records a failure for <paramref name="source"/> and returns how long to delay before answering, and
    /// whether this failure is the one that crossed the threshold.
    /// </summary>
    /// <remarks>
    /// <b>Reporting the crossing separately is what keeps the audit log readable.</b> An enumeration run
    /// produces thousands of failures; auditing every one past the threshold would bury the signal in the
    /// noise it is reporting, which is the defect the audit view spent 3.21 recovering from.
    /// </remarks>
    public (TimeSpan Delay, bool JustTripped) RecordFailure(string source, InvitationOptions options)
    {
        if (options.ThrottleFailureThreshold <= 0) return (TimeSpan.Zero, false);

        var now = timeProvider.GetUtcNow();
        var attempts = _bySource.AddOrUpdate(
            source,
            _ => new Attempts(1, now),
            (_, existing) => now - existing.WindowStart > options.ThrottleWindow
                ? new Attempts(1, now)
                : existing with { Count = existing.Count + 1 });

        var over = attempts.Count - options.ThrottleFailureThreshold;
        if (over <= 0) return (TimeSpan.Zero, false);

        return (DelayFor(over, options.MaxThrottleDelay), over == 1);
    }

    /// <summary>
    /// Doubling from a quarter-second, capped. Doubling because the first excess failure should barely be
    /// noticed by a person who miskeyed a link, while the hundredth should cost a script real time.
    /// </summary>
    private static TimeSpan DelayFor(int over, TimeSpan max)
    {
        if (max <= TimeSpan.Zero) return TimeSpan.Zero;

        // Shifting past the cap would overflow long before it, so clamp the exponent first.
        var steps = Math.Min(over - 1, 20);
        var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, steps));

        return delay > max ? max : delay;
    }

    private readonly record struct Attempts(int Count, DateTimeOffset WindowStart);
}
