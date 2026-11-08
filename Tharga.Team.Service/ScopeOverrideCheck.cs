using Microsoft.Extensions.Hosting;

namespace Tharga.Team.Service;

/// <summary>
/// Refuses to start when a host's scope override names a scope nobody registered, or cannot be applied
/// (Tharga/Team#405).
/// </summary>
/// <remarks>
/// <b>Throws rather than warns, unlike <see cref="UnregisteredRoleScopeCheck"/>.</b> A mistyped override changes
/// nothing and says nothing, and the host believes <c>apikey:manage</c> is now held only by people granted it while
/// every Administrator still holds it. For a setting whose whole purpose is to narrow who holds a scope, silence is
/// the failure that matters. It runs at startup because an override may be given before the module that registers
/// the scope, so the name can only be checked once everything has registered.
/// </remarks>
internal sealed class ScopeOverrideCheck(IScopeRegistry scopes) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (scopes is not ScopeRegistry registry) return Task.CompletedTask;

        var unmatched = registry.UnmatchedOverrides;
        if (unmatched.Count > 0)
            throw new InvalidOperationException(
                $"A scope override names scopes that are not registered: {string.Join(", ", unmatched)}. " +
                "Check the spelling, and that the module registering the scope is added.");

        // Reading the scopes applies every override, so one that cannot be applied fails here and not on a request.
        _ = registry.All;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
