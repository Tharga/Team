using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// A circuit's session checks, each in a scope of its own, created when the check runs and disposed when it is done (Tharga/Team#471), as the claim
/// revalidation does.
/// </summary>
/// <remarks>
/// <b>Not the circuit's own scope</b>: the watch wakes on a timer thread, and must not share the circuit's scoped services (a host's policy, its user service)
/// with a render in flight. Whether sessions are on is read from the registrations and options, exactly as <see cref="SessionEvaluator.Active"/> decides it,
/// so asking costs no scope.
/// </remarks>
internal sealed class SessionCircuitScope(IServiceScopeFactory scopeFactory, IServiceProviderIsService registered, IOptions<ThargaBlazorOptions> options) : ICircuitSessionChecks
{
    public bool Active => registered.IsService(typeof(ISessionPolicy)) && (options.Value.Sessions ?? new SessionOptions()).Enabled;

    public Task<SessionCheck> InspectCircuitAsync(ClaimsPrincipal principal, string activeTeamKey, CancellationToken cancellationToken = default)
        => RunAsync(x => x.InspectCircuitAsync(principal, activeTeamKey, cancellationToken));

    public Task<bool> ConfirmPresenceAsync(ClaimsPrincipal principal, string activeTeamKey, CancellationToken cancellationToken = default)
        => RunAsync(x => x.ConfirmPresenceAsync(principal, activeTeamKey, cancellationToken));

    public Task<bool> SignOutAsync(ClaimsPrincipal principal, string userKey = null, CancellationToken cancellationToken = default)
        => RunAsync(x => x.SignOutAsync(principal, userKey, cancellationToken));

    private async Task<T> RunAsync<T>(Func<SessionEvaluator, Task<T>> check)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await check(scope.ServiceProvider.GetRequiredService<SessionEvaluator>());
    }
}
