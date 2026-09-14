namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>AccessSimulationIndicator</c> — the compact marker a host can place beside
/// the team selector.
/// </summary>
/// <remarks>
/// The tooltip reuses <see cref="AccessSimulationBarText.ViewingAs"/> and its target phrases, so the indicator and
/// the banner describe a simulation in the same words.
/// </remarks>
public static class AccessSimulationIndicatorText
{
    public static readonly TextKey Label = new("team.simulation.indicator.label", "Access reduced");

    public static readonly TextKey Stop = new("team.simulation.indicator.stop", "Return to my access");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Label, Stop];
}
