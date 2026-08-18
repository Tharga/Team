namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// The two grants behind access simulation. They are separate because they are not the same capability.
/// </summary>
/// <remarks>
/// <b>Splitting them is the fix for Tharga/Team#223.</b> Both halves used to sit behind
/// <see cref="Simulate"/>, registered at <c>AccessLevel.Administrator</c> — and Owner and Administrator
/// receive every registered scope, so the grant reached every team owner and administrator in every tenant,
/// with no way for a host to narrow it.
/// </remarks>
public static class SimulationScopes
{
    /// <summary>
    /// <b>Run as</b> — view the application as another member, access level, role or scope set. A
    /// <b>team</b> scope, registered at <c>Administrator</c>, and deliberately so: checking what a Viewer
    /// sees before inviting one is an ordinary thing for a team owner to want.
    /// </summary>
    public const string Simulate = "simulation:use";

    /// <summary>
    /// <b>Demo mode</b> — drop your own system scopes and application roles. A <b>system</b> scope.
    /// </summary>
    /// <remarks>
    /// <b>System rather than team, because that is what the operation does.</b> It removes system scopes and
    /// application roles, so for a caller holding none — every customer's own team owner — it offers to drop
    /// nothing. It was inert for exactly the audience that used to see it.
    /// <para>
    /// Resolve it with <c>TeamScopeGate.HasSystemScope</c>, never a bare <c>HasClaim</c>: an in-team claim of
    /// this name must not satisfy it, the same rule <c>teams:delete</c> and <c>teams:set-owner</c> follow.
    /// </para>
    /// </remarks>
    public const string Demo = "simulation:demo";
}
