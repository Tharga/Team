using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Publishes the circuit's principal for the length of each inbound activity, so an audit entry written
/// by a component records the simulation it was written under.
/// </summary>
/// <remarks>
/// <b>Every interaction in a circuit arrives as an inbound activity</b> — an event handler, a
/// <c>StateHasChanged</c>, a JS interop callback — which is why the principal is published here rather
/// than once when the circuit opens: an <see cref="AsyncLocal{T}"/> set at circuit start does not flow
/// into the separate call stacks the renderer later begins.
/// <para>
/// Scoped, like every <see cref="CircuitHandler"/>, so it may hold the circuit's own
/// <see cref="AuthenticationStateProvider"/>. The accessor it writes to is the singleton, which is what
/// the enricher can reach.
/// </para>
/// </remarks>
internal sealed class AccessSimulationCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    AccessSimulationPrincipalAccessor principalAccessor,
    ILogger<AccessSimulationCircuitHandler> logger = null) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
        => async context =>
        {
            using (principalAccessor.Push(await ResolvePrincipalAsync()))
            {
                await next(context);
            }
        };

    /// <remarks>
    /// Reading the state must not become a new way for an interaction to fail. Recording context is the
    /// whole purpose here, and an entry that says nothing about a simulation is a far smaller loss than a
    /// circuit that cannot dispatch the event which would have produced it.
    /// </remarks>
    private async Task<ClaimsPrincipal> ResolvePrincipalAsync()
    {
        try
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            return state?.User;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not read the authentication state for an inbound circuit activity. Audit entries written by it will not record whether a simulation was active.");
            return null;
        }
    }
}
