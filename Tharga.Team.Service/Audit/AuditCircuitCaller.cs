using System.Security.Claims;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// The person behind the interactive Blazor Server circuit activity running on this flow, for audit entries written where there is no request to
/// read them from (Tharga/Team#572).
/// </summary>
/// <remarks>
/// <b>Why it exists.</b> <c>AuditHelper.BuildEntry</c> read the caller from the HTTP request alone, and a circuit has none after its first
/// request, so every entry written from an interactive page named nobody and notifications said "an unknown caller". The circuit's principal
/// lives in its scoped <c>AuthenticationStateProvider</c>, which the static entry builder and the singleton entry factory cannot reach, so
/// <c>Tharga.Team.Blazor</c> publishes it here for each inbound circuit activity.
/// <para>
/// <b>It flows like any <see cref="AsyncLocal{T}"/></b> and is restored, never cleared, when an activity ends, because ASP.NET Core wraps only
/// the start of an event handler and the rest of an async handler runs after the activity returns. The flow carries a <see cref="Holder"/>
/// rather than the principal itself: the circuit keeps one, refreshes it on each activity and clears it when the circuit closes, so a timer or a
/// loop started during an activity never names the person once their circuit is gone.
/// </para>
/// <para>
/// <b>Internal, so it cannot be forged.</b> Only <c>Tharga.Team.Blazor</c> publishes it; host code cannot set who an entry is attributed to.
/// It is read for audit only and authorizes nothing.
/// </para>
/// </remarks>
internal static class AuditCircuitCaller
{
    private static readonly AsyncLocal<Holder> _current = new();

    /// <summary>The caller of the circuit activity on this flow, or null when there is none or its circuit has closed.</summary>
    internal static Caller Current => _current.Value?.Caller;

    /// <summary>Makes <paramref name="holder"/> the one this flow reads until the returned scope is disposed, then restores whatever was in effect before.</summary>
    internal static IDisposable Use(Holder holder)
    {
        var previous = _current.Value;
        _current.Value = holder;
        return new Scope(previous);
    }

    /// <summary>A principal and the identity resolver of the host it came from, read together.</summary>
    internal sealed record Caller(ClaimsPrincipal Principal, UserIdentityResolver Resolver);

    /// <summary>One circuit's published caller, shared by every flow its activities start.</summary>
    internal sealed class Holder
    {
        private volatile Caller _caller;

        public Caller Caller => _caller;

        /// <summary>The circuit's caller as it is now; a null principal publishes none, so an activity whose state could not be read names nobody.</summary>
        public void Update(ClaimsPrincipal principal, UserIdentityResolver resolver)
            => _caller = principal == null ? null : new Caller(principal, resolver ?? UserIdentityResolver.Default);

        /// <summary>The circuit has closed: every flow that still carries this holder names nobody from now on.</summary>
        public void Clear() => _caller = null;
    }

    private sealed class Scope(Holder previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            // Restore rather than clear, as AuditContextAccessor does, and guarded against a double dispose restoring a stale holder.
            if (_disposed) return;
            _disposed = true;
            _current.Value = previous;
        }
    }
}
