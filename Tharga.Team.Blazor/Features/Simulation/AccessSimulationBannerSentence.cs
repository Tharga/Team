using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Builds the banner's wording from resolved text: what is being simulated, and the sentence around it.
/// </summary>
/// <remarks>
/// Split out of the component so the placeholder handling can be tested. A banner that throws while
/// rendering would take away the control someone uses to end a simulation, and the template it renders is
/// host-supplied translation the toolkit does not control.
/// </remarks>
internal static class AccessSimulationBannerSentence
{
    private const string Placeholder = "{0}";

    /// <summary>
    /// The text either side of the target, so the component can emphasise the target between them.
    /// </summary>
    /// <remarks>
    /// <b>Split rather than <see cref="TextSet.Format"/></b>, because the target is rendered as markup and a
    /// formatted string cannot carry any. A template with no placeholder returns the whole sentence as
    /// <c>Before</c>, which renders it unchanged and simply names nobody — the safe direction for a
    /// translation that dropped the <c>{0}</c>.
    /// </remarks>
    public static (string Before, string After) SplitAround(string template)
    {
        if (string.IsNullOrEmpty(template)) return (string.Empty, string.Empty);

        var index = template.IndexOf(Placeholder, StringComparison.Ordinal);
        if (index < 0) return (template, string.Empty);

        return (template[..index], template[(index + Placeholder.Length)..]);
    }

    /// <summary>What to call the thing being simulated.</summary>
    /// <remarks>
    /// A member is named rather than described: their name is the whole answer, and there is no English
    /// around it to translate. A role and an access level need a phrase, and each has its own key so the
    /// phrase can be rebuilt rather than assembled from parts.
    /// </remarks>
    public static string Describe(AccessSimulation simulation, TextSet text) => simulation.Kind switch
    {
        AccessSimulationKind.Role => text.Format(AccessSimulationBarText.TargetRole, simulation.Label),
        AccessSimulationKind.AccessLevel => text.Format(AccessSimulationBarText.TargetAccessLevel, simulation.Label),
        _ => simulation.Label
    };
}
