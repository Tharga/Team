namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Whether <c>AccessSimulationIndicator</c> renders.
/// </summary>
/// <remarks>
/// Pure and static so it is unit-testable; the project has no bUnit, so a rule left in markup cannot be tested.
/// </remarks>
internal static class AccessSimulationIndicatorGate
{
    /// <summary>
    /// True while a simulation is active, unless it is a demo.
    /// </summary>
    /// <remarks>
    /// <b>Hidden during demo mode</b>, for the reason the banner is: a demo is meant to look like an ordinary
    /// member's session, and a marker saying access is reduced gives it away (Tharga/Team#223). The profile card
    /// remains the way out of a demo.
    /// </remarks>
    /// <param name="enabled">Whether the host turned simulation on.</param>
    /// <param name="active">The simulation in force for the selected team, or null.</param>
    public static bool Show(bool enabled, AccessSimulation active)
        => enabled && active != null && active.Kind != AccessSimulationKind.Demo;
}
