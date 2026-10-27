using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Whether a component should offer an action, given the caller's claims. Mirrors what the service layer
/// enforces, so a surface does not render controls the server will reject.
/// </summary>
/// <remarks>
/// The distinction this exists to make explicit: an in-team scope is issued for the selected team only,
/// so a bare <c>HasClaim(Scope, x)</c> answers "holds it somewhere", not "holds it here". Gating on the
/// bare claim is how the API key view came to render for a caller with no access to the selected team —
/// its <c>apikey:manage</c> came from a system role, which is team-independent by design. Use
/// <see cref="HasTeamScope"/> for anything acting on a team, and <see cref="HasSystemScope"/> only where
/// the operation genuinely spans the system.
/// </remarks>
public static class TeamScopeGate
{
    /// <summary>
    /// Whether the caller holds <paramref name="scope"/> <b>for</b> <paramref name="teamKey"/> — the claim
    /// is present and it was issued for that team.
    /// </summary>
    /// <remarks>
    /// The service layer's own rule, <c>TeamScopePolicy.HasTeamScope</c>, rather than a copy of it: a gate that
    /// restates the rule can come to answer differently from the check it is meant to mirror.
    /// </remarks>
    public static bool HasTeamScope(ClaimsPrincipal principal, string scope, string teamKey)
        => TeamScopePolicy.HasTeamScope(principal, scope, teamKey);

    /// <summary>
    /// Whether the caller holds the system <paramref name="scope"/> — granted by an app role or a system API
    /// key, not by an access level on some team. Use for operations that genuinely act across the system.
    /// </summary>
    public static bool HasSystemScope(ClaimsPrincipal principal, string scope)
        => principal?.HasClaim(TeamClaimTypes.SystemScope, scope) ?? false;
}
