using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Fails startup when a host registered its own <see cref="IUserSessionNotifier"/> or <see cref="ITeamEntryNotifier"/> (Tharga/Team#471).
/// </summary>
/// <remarks>
/// <b>The notifiers are the toolkit's own caches</b>, because a notification must make the cache forget what it remembered before anybody is told; one that
/// is not the cache would leave every instance answering from what it remembered, and the live circuits listening to a notifier the toolkit never raises.
/// A host listens by subscribing to them and relays by calling their <c>Notify</c> with <c>FromRemote</c> set, so it never needs its own. It throws, like
/// <see cref="SessionWiringCheck"/>, rather than replacing the host's registration silently.
/// </remarks>
internal sealed class SessionNotifierWiringCheck(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Require<IUserSessionNotifier, SessionCache>();
        Require<ITeamEntryNotifier, TeamEntryCache>();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Require<TNotifier, TCache>()
    {
        var notifier = services.GetService<TNotifier>();
        var cache = services.GetService<TCache>();
        if (notifier == null || cache == null || ReferenceEquals(notifier, cache)) return;

        throw new InvalidOperationException(
            $"A {typeof(TNotifier).Name} of the host's own ({notifier.GetType().Name}) is registered. The toolkit's {typeof(TNotifier).Name} is its cache, which " +
            $"forgets what it remembered before anybody is told, so it cannot be replaced: remove the registration, subscribe to the toolkit's to listen, and " +
            $"call its Notify with FromRemote set to relay what arrives from other instances.");
    }
}
