using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Starts the circuit's <see cref="ISessionCircuit"/> when the circuit opens and stops it when it closes, so the session is watched even where no component
/// asks (Tharga/Team#471).
/// </summary>
/// <remarks>It never delays the circuit opening: the watch is started without being awaited, and a failure to start it is the circuit's own to log.</remarks>
internal sealed class SessionCircuitHandler(ISessionCircuit watch, ILogger<SessionCircuitHandler> logger = null) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _ = StartWatchAsync();
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        watch.Dispose();
        return Task.CompletedTask;
    }

    private async Task StartWatchAsync()
    {
        try
        {
            await watch.StartAsync();
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The live session watch could not start; the session is still checked on each request.");
        }
    }
}
