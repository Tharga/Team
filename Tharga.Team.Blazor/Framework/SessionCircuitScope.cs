using Microsoft.Extensions.DependencyInjection;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// The services a circuit's session watch reads the store and the host's policy through, in a scope of its own for the life of the circuit
/// (Tharga/Team#471).
/// </summary>
/// <remarks>
/// <b>Not the circuit's own scope</b>, for the reason the claim revalidation gives: the watch wakes on a timer thread, and must not share the circuit's scoped
/// services (a host's policy, its user service) with a render in flight. Disposed with the circuit's scope.
/// </remarks>
internal sealed class SessionCircuitScope(IServiceScopeFactory scopeFactory) : IDisposable
{
    private readonly IServiceScope _scope = scopeFactory.CreateScope();

    public SessionEvaluator Evaluator => _scope.ServiceProvider.GetRequiredService<SessionEvaluator>();

    public void Dispose() => _scope.Dispose();
}
