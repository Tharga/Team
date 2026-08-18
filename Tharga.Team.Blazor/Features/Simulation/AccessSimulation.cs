using Tharga.Team;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>How the simulated access was named.</summary>
/// <remarks>
/// Carried only so the indicator can say what is being simulated, and so the difference report can
/// explain <i>why</i> something could not be reproduced. It never affects what is removed — every kind
/// resolves to a target scope set and is applied identically.
/// </remarks>
public enum AccessSimulationKind
{
    /// <summary>Another member of the selected team.</summary>
    User,

    /// <summary>A tenant role, registered or runtime-defined.</summary>
    Role,

    /// <summary>An explicit list of scopes.</summary>
    Scopes,

    /// <summary>An access level.</summary>
    AccessLevel,

    /// <summary>
    /// Dropping your own system scopes and application roles, to see the application as an ordinary tenant
    /// user does.
    /// </summary>
    /// <remarks>
    /// <b>A kind of its own rather than a <see cref="Scopes"/> simulation carrying a known label.</b> It
    /// used to be the latter, so the only way to tell a demo from any other scope-set simulation was to
    /// match the string "Demo mode" — and the visibility rules have to tell them apart: a demo shows nothing
    /// in the navigation bar, because a banner announcing it defeats the point of demonstrating the product.
    /// </remarks>
    Demo
}

/// <summary>
/// An active access simulation: the access a caller has asked to be shown as, before it is intersected
/// with what they actually hold.
/// </summary>
/// <remarks>
/// <b>This is a request, not a grant.</b> Nothing here is trusted — it arrives from a cookie the caller
/// can edit. It is safe because <see cref="AccessSimulationFilter"/> only ever removes what the caller
/// holds; naming a scope here that the caller does not hold achieves nothing.
/// </remarks>
public sealed record AccessSimulation
{
    /// <summary>How the target was named. Presentation only.</summary>
    public required AccessSimulationKind Kind { get; init; }

    /// <summary>What to call it in the indicator — a member's name, a role name, a level.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// The target's scopes. The caller keeps the ones they also hold and loses the rest.
    /// </summary>
    /// <remarks>
    /// <b>Record equality compares this by reference, not by content</b>, so two simulations carrying the
    /// same scopes are not equal. Nothing compares simulations today — the revalidator compares claims —
    /// but do not add an equality check on this type without fixing that first.
    /// </remarks>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>
    /// The target's access level, when the target has one. Applied only if it is a de-escalation.
    /// </summary>
    public AccessLevel? AccessLevel { get; init; }

    /// <summary>
    /// Whether to drop the caller's system scopes.
    /// </summary>
    /// <remarks>
    /// Set when simulating a <see cref="AccessSimulationKind.User"/>. System scopes come from app roles
    /// issued by the identity provider, which the toolkit does not store, so another user's system scopes
    /// cannot be computed. Keeping the caller's own would show access the target may not have; dropping
    /// them is the safe direction, and the difference report says the system half is not reproduced.
    /// </remarks>
    public bool DropSystemScopes { get; init; }

    /// <summary>
    /// Whether to drop the caller's application roles, for the same reason as
    /// <see cref="DropSystemScopes"/>.
    /// </summary>
    public bool DropAppRoles { get; init; }
}
