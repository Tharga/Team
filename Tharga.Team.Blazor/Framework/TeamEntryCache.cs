using System.Collections.Concurrent;
using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// What the team entry guard last said, per user, sign-in session and team, for a short time, on this instance.
/// </summary>
/// <remarks>
/// <b>Keyed by the session as well as the user and the team</b>, because the answer depends on how the person signed in: one who has stepped up in
/// one browser must not carry that to another that has not. The session is told apart by the library's own session claim when sessions are on, else by
/// the claims the sign-in left on the principal (<c>sid</c>, else <c>auth_time</c>, <c>idp</c> and <c>amr</c>), which a host's guard reads too.
/// <para>
/// Per instance, so <see cref="InvalidateAsync"/> reaches this instance's circuits at once and another instance when its answer expires. That is why the
/// evaluator never answers a step-up from here: the caller completes it on whichever instance takes the callback.
/// </para>
/// </remarks>
internal sealed class TeamEntryCache(TimeProvider timeProvider) : ITeamEntryGuardState
{
    private readonly ConcurrentDictionary<Key, Entry> _entries = new();

    /// <summary>Raised after answers were forgotten for a user, with the team when only one was, so a live circuit can re-evaluate now.</summary>
    public event EventHandler<TeamEntryInvalidatedEventArgs> Invalidated;

    public bool TryGet(string userKey, string session, string teamKey, out TeamEntryDecision decision)
    {
        decision = null;
        var key = new Key(userKey, session, teamKey);
        if (!_entries.TryGetValue(key, out var entry)) return false;

        if (entry.Expires <= timeProvider.GetUtcNow())
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        decision = entry.Decision;
        return true;
    }

    public void Set(string userKey, string session, string teamKey, TeamEntryDecision decision, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;

        _entries[new Key(userKey, session, teamKey)] = new Entry(decision, timeProvider.GetUtcNow() + duration);
        if (_entries.Count > 10_000) Sweep();
    }

    public Task InvalidateAsync(string userKey, string teamKey = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey)) return Task.CompletedTask;

        foreach (var key in _entries.Keys.Where(x => x.User == userKey && (teamKey == null || x.Team == teamKey)).ToArray())
            _entries.TryRemove(key, out _);

        Invalidated?.Invoke(this, new TeamEntryInvalidatedEventArgs(userKey, teamKey));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Tells the sign-in sessions of one person apart: the library's own session claim when there is one (Tharga/Team#471), issued by the server and unique
    /// per sign-in; else the identity provider's <c>sid</c>, which may be shared or absent; otherwise what the sign-in proved.
    /// </summary>
    public static string SessionOf(ClaimsPrincipal principal)
    {
        var session = principal?.FindFirst(TeamClaimTypes.SessionId)?.Value;
        if (!string.IsNullOrEmpty(session)) return session;

        var sid = principal?.FindFirst("sid")?.Value;
        if (!string.IsNullOrEmpty(sid)) return sid;

        return string.Join('|',
            principal?.FindFirst("auth_time")?.Value,
            principal?.FindFirst("idp")?.Value,
            string.Join(',', (principal?.FindAll("amr") ?? []).Select(x => x.Value).Order(StringComparer.Ordinal)));
    }

    private void Sweep()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var pair in _entries.Where(x => x.Value.Expires <= now).ToArray()) _entries.TryRemove(pair.Key, out _);
    }

    private readonly record struct Key(string User, string Session, string Team);

    private readonly record struct Entry(TeamEntryDecision Decision, DateTimeOffset Expires);
}

/// <summary>The answers for a user were forgotten.</summary>
internal sealed class TeamEntryInvalidatedEventArgs(string userKey, string teamKey) : EventArgs
{
    public string UserKey { get; } = userKey;

    /// <summary>The team only that team's answers were forgotten for, or null for every team.</summary>
    public string TeamKey { get; } = teamKey;
}
