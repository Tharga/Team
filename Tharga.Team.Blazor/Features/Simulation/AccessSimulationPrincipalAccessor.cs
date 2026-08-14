using System.Security.Claims;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// The principal of the circuit activity currently running, for singletons that cannot be given one.
/// </summary>
/// <remarks>
/// <see cref="AccessSimulationAuditEnricher"/> is a singleton, because
/// <see cref="Tharga.Team.Service.Audit.CompositeAuditLogger"/> is one and captures its enrichers at
/// construction. In a circuit there is no <c>HttpContext</c> to read the caller from, and the circuit's
/// <c>AuthenticationStateProvider</c> is scoped — so the principal is published here for the length of
/// each inbound activity by <see cref="AccessSimulationCircuitHandler"/> instead.
/// <para>
/// This is the same shape, for the same reason, as
/// <see cref="Tharga.Team.Service.Audit.AuditContextAccessor"/>: ambient context is what
/// <see cref="AsyncLocal{T}"/> exists for, and it flows across <c>await</c> without cooperation from the
/// code in between.
/// </para>
/// <para>
/// <b>Read-only context, never a grant.</b> Nothing authorizes from this — the simulation it carries is
/// recorded, not enforced, and enforcement stays where it already is, on the claims themselves.
/// </para>
/// </remarks>
internal sealed class AccessSimulationPrincipalAccessor
{
    private readonly AsyncLocal<ClaimsPrincipal> _current = new();

    /// <summary>The principal of the activity running on this flow, or null.</summary>
    public ClaimsPrincipal Current => _current.Value;

    /// <summary>
    /// Makes <paramref name="principal"/> the principal for the current async flow until the returned
    /// scope is disposed, then restores whatever was in effect before.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="principal"/> is a legitimate value rather than an argument error: an
    /// activity whose authentication state could not be read has no principal, and must still shadow an
    /// outer one rather than inherit it.
    /// </remarks>
    public IDisposable Push(ClaimsPrincipal principal)
    {
        var previous = _current.Value;
        _current.Value = principal;
        return new Scope(this, previous);
    }

    private sealed class Scope(AccessSimulationPrincipalAccessor owner, ClaimsPrincipal previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            // Restoring rather than clearing is what keeps a nested scope from dropping its parent's
            // principal; the guard stops a double dispose restoring a stale one over what is in effect.
            if (_disposed) return;
            _disposed = true;
            owner._current.Value = previous;
        }
    }
}
