using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// The sessions read from the store, and the policy's limits for each in a team, for a short time, on this instance (Tharga/Team#471). Shaped like
/// <see cref="TeamEntryCache"/>.
/// </summary>
/// <remarks>
/// <b>Per instance, and only a cache</b>: what holds across instances is the store, whose writes are conditional. A session updated here, because this
/// instance recorded activity or ended it, keeps the time it was read until, so a session that stays busy is still read again once per window and an end
/// made elsewhere is noticed.
/// </remarks>
internal sealed class SessionCache(TimeProvider timeProvider, ILogger<SessionCache> logger = null) : IUserSessionNotifier
{
    private const int SweepAbove = 10_000;

    private readonly ConcurrentDictionary<string, Entry<UserSession>> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<LimitsKey, Entry<SessionLimits>> _limits = new();

    /// <inheritdoc />
    public event EventHandler<UserSessionEndedEventArgs> SessionEnded;

    /// <summary>
    /// Forgets the remembered sessions of the person first, so a handler that looks reads the store and not the old answer, then tells every handler in turn.
    /// A throwing handler is logged and skipped: the end stands, only the notification failed.
    /// </summary>
    public void Notify(UserSessionEndedEventArgs args)
    {
        if (args == null) return;

        ForgetSessionsOf(args.UserKey);

        var handlers = SessionEnded;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<UserSessionEndedEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "A session-ended notification handler threw. The end stands; only the notification failed.");
            }
        }
    }

    public bool TryGetSession(string sessionId, out UserSession session)
    {
        session = null;
        if (sessionId == null || !_sessions.TryGetValue(sessionId, out var entry)) return false;

        if (entry.Expires <= timeProvider.GetUtcNow())
        {
            _sessions.TryRemove(sessionId, out _);
            return false;
        }

        session = entry.Value;
        return true;
    }

    public void SetSession(UserSession session, TimeSpan duration)
    {
        if (session?.SessionId == null || duration <= TimeSpan.Zero) return;

        _sessions[session.SessionId] = new Entry<UserSession>(session, timeProvider.GetUtcNow() + duration);
        if (_sessions.Count > SweepAbove) Sweep(_sessions);
    }

    /// <summary>Replaces a remembered session with what this instance just wrote, keeping when it expires; nothing when it is not remembered.</summary>
    public void UpdateSession(UserSession session)
    {
        if (session?.SessionId == null || !_sessions.TryGetValue(session.SessionId, out var entry)) return;

        _sessions.TryUpdate(session.SessionId, entry with { Value = session }, entry);
    }

    public void ForgetSession(string sessionId)
    {
        if (sessionId != null) _sessions.TryRemove(sessionId, out _);
    }

    public bool TryGetLimits(string sessionId, string teamKey, out SessionLimits limits)
    {
        limits = null;
        var key = new LimitsKey(sessionId, teamKey);
        if (!_limits.TryGetValue(key, out var entry)) return false;

        if (entry.Expires <= timeProvider.GetUtcNow())
        {
            _limits.TryRemove(key, out _);
            return false;
        }

        limits = entry.Value;
        return true;
    }

    public void SetLimits(string sessionId, string teamKey, SessionLimits limits, TimeSpan duration)
    {
        if (sessionId == null || duration <= TimeSpan.Zero) return;

        _limits[new LimitsKey(sessionId, teamKey)] = new Entry<SessionLimits>(limits, timeProvider.GetUtcNow() + duration);
        if (_limits.Count > SweepAbove) Sweep(_limits);
    }

    private void ForgetSessionsOf(string userKey)
    {
        if (userKey == null) return;

        foreach (var pair in _sessions.Where(x => x.Value.Value.UserKey == userKey).ToArray())
        {
            _sessions.TryRemove(pair.Key, out _);
        }
    }

    private void Sweep<TKey, TValue>(ConcurrentDictionary<TKey, Entry<TValue>> entries)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var pair in entries.Where(x => x.Value.Expires <= now).ToArray())
        {
            entries.TryRemove(pair.Key, out _);
        }
    }

    private readonly record struct LimitsKey(string Session, string Team);

    private readonly record struct Entry<TValue>(TValue Value, DateTimeOffset Expires);
}
