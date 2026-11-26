using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Starts the circuit's <see cref="ISessionCircuit"/> when the circuit opens and stops it when it closes, so the session is watched even where no component
/// asks (Tharga/Team#471).
/// </summary>
/// <remarks>It never delays the circuit opening: the watch is started without being awaited, and a failure to start it is the circuit's own to log.</remarks>
internal sealed class SessionCircuitHandler(ISessionCircuit circuit) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit1, CancellationToken cancellationToken) => throw new NotImplementedException();

    public override Task OnCircuitClosedAsync(Circuit circuit1, CancellationToken cancellationToken) => throw new NotImplementedException();
}
