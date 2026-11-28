using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Publishes the circuit's principal for each inbound activity, so an audit entry written by a component names the person who made it
/// (Tharga/Team#572) and records any access simulation it was made under (Tharga/Team#220).
/// </summary>
/// <remarks>
/// <b>Every interaction in a circuit arrives as an inbound activity</b>: an event handler, a navigation, a JS interop callback, the first render.
/// An <see cref="AsyncLocal{T}"/> set when the circuit opens does not flow into the separate call stacks the renderer later begins, so the
/// principal is published for each one. It flows on into the awaits of a handler that outlive the activity, because ASP.NET Core wraps only the
/// start of an event handler.
/// <para>
/// <b>One holder per circuit</b>, refreshed on each activity so it carries the claims as they are then, and cleared when the circuit closes, so
/// work started during an activity names nobody once the person's circuit is gone.
/// </para>
/// <para>
/// <b>Outermost</b>, so every other handler's activity code runs with the caller already published. Scoped, like every
/// <see cref="CircuitHandler"/>, so it may hold the circuit's own <see cref="AuthenticationStateProvider"/>.
/// </para>
/// </remarks>
internal sealed class AuditCallerCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    UserIdentityResolver identityResolver = null,
    ILogger<AuditCallerCircuitHandler> logger = null) : CircuitHandler
{
    private readonly AuditCircuitCaller.Holder _holder = new();

    public override int Order => int.MinValue;

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next)
        => async context =>
        {
            _holder.Update(await ResolvePrincipalAsync(), identityResolver);

            using (AuditCircuitCaller.Use(_holder))
            {
                await next(context);
            }
        };

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _holder.Clear();
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Reading the state must not become a new way for an interaction to fail. An entry that names nobody is a far smaller loss than a circuit
    /// that cannot dispatch the event which would have produced it.
    /// </remarks>
    private async Task<ClaimsPrincipal> ResolvePrincipalAsync()
    {
        try
        {
#pragma warning disable BL0013
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
#pragma warning restore BL0013
            return state?.User;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not read the authentication state for an inbound circuit activity. Audit entries written by it will not name the caller.");
            return null;
        }
    }
}
