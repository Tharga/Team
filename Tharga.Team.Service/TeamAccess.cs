namespace Tharga.Team.Service;

/// <summary>Why a database call is permitted to run.</summary>
public enum TeamAccessKind
{
    /// <summary>Authorized against one team, named by <see cref="TeamAccessContext.TeamKey"/>.</summary>
    Team,

    /// <summary>Authorized by a system scope, spanning no particular team.</summary>
    System,

    /// <summary>Deliberately unauthorized — startup seeding, migrations, background work.</summary>
    Unchecked
}

/// <summary>
/// The record that an authorization decision was made for the current call flow.
/// </summary>
public sealed record TeamAccessContext
{
    internal TeamAccessContext(TeamAccessKind kind, string teamKey, string reason)
    {
        Kind = kind;
        TeamKey = teamKey;
        Reason = reason;
    }

    public TeamAccessKind Kind { get; }

    /// <summary>The team the call was authorized against, or null for system and unchecked access.</summary>
    public string TeamKey { get; }

    /// <summary>Why access was granted. Always set for system and unchecked access, and for team access admitted by a system grant.</summary>
    public string Reason { get; }
}

/// <summary>
/// Ambient record of the authorization decision covering the current call flow, read by
/// <see cref="TeamAccessInterceptor"/> at the database boundary.
/// </summary>
/// <remarks>
/// The authorization layer opens a scope for you, so a correctly registered service never touches this
/// type. It exists for the deliberate exceptions — work that legitimately reaches the database without a
/// caller to authorize, such as startup seeding or a background job — which must say so explicitly:
/// <code>
/// using var _ = TeamAccess.System("nightly audit retention");
/// </code>
/// A reason is required on those paths precisely so the escape hatch stays greppable and reviewable.
/// <para>
/// Backed by <see cref="AsyncLocal{T}"/> rather than a DI-scoped service: in Blazor Server a DI scope is
/// the circuit's lifetime, not the operation's, so a scoped holder would keep one team's authorization
/// alive across the whole circuit and go stale the moment the user switched team. An
/// <see cref="AsyncLocal{T}"/> flows down through awaits into the work it covers and is restored when the
/// scope disposes, which matches the shape of an authorization decision.
/// </para>
/// </remarks>
public static class TeamAccess
{
    private static readonly AsyncLocal<TeamAccessContext> _current = new();

    /// <summary>The decision covering the current call flow, or null when nothing has authorized it.</summary>
    public static TeamAccessContext Current => _current.Value;

    /// <summary>Records that the caller was authorized against <paramref name="teamKey"/>.</summary>
    public static IDisposable ForTeam(string teamKey)
        => Enter(new TeamAccessContext(TeamAccessKind.Team, teamKey, null));

    internal static IDisposable ForTeam(string teamKey, string reason)
        => Enter(new TeamAccessContext(TeamAccessKind.Team, teamKey, Require(reason)));

    /// <summary>Records that the caller holds a system scope, spanning no particular team.</summary>
    public static IDisposable System(string reason)
        => Enter(new TeamAccessContext(TeamAccessKind.System, null, Require(reason)));

    /// <summary>
    /// Declares that this call flow deliberately reaches the database without an authorization check.
    /// For work with no caller to authorize — seeding, migrations, background jobs.
    /// </summary>
    public static IDisposable Unchecked(string reason)
        => Enter(new TeamAccessContext(TeamAccessKind.Unchecked, null, Require(reason)));

    private static IDisposable Enter(TeamAccessContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new Scope(previous);
    }

    private static string Require(string reason)
        => string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("A reason is required so the exception stays greppable and reviewable.", nameof(reason))
            : reason;

    private sealed class Scope(TeamAccessContext previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = previous;
        }
    }
}
