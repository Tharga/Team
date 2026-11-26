using System.Security.Claims;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// What a circuit's session watch asks of the session check (Tharga/Team#471): <see cref="SessionEvaluator"/> answers it, and <see cref="SessionCircuitScope"/>
/// asks a fresh evaluator per call.
/// </summary>
internal interface ICircuitSessionChecks
{
    /// <summary>Whether a policy is registered and sessions are switched on.</summary>
    bool Active { get; }

    /// <summary>Reads the circuit's session afresh and ends it when over its limits; a failure propagates.</summary>
    Task<SessionCheck> InspectCircuitAsync(ClaimsPrincipal principal, string activeTeamKey, CancellationToken cancellationToken = default);

    /// <summary>Records that the person answered "Still here?"; false when the session is not there, has ended or is already past a limit.</summary>
    Task<bool> ConfirmPresenceAsync(ClaimsPrincipal principal, string activeTeamKey, CancellationToken cancellationToken = default);

    /// <summary>Ends the session as signed out; true when this call ended it.</summary>
    Task<bool> SignOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
