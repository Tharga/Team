namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// The team page's "show only the selected team" option: when it narrows the list, and when it offers the way back
/// (Tharga/Team#400).
/// </summary>
/// <remarks>
/// Presentation only, like <see cref="TeamAccessFilter"/> -- it hides rows the caller could already see and grants
/// or withholds nothing. Pure and static so it is unit-testable.
/// </remarks>
internal static class TeamSelectionFilter
{
    /// <summary>
    /// Whether the list is narrowed to the selected team: the option is on, the caller has not asked for all teams,
    /// and the selected team is among those listed. With no selection, or a selection that is not listed (hidden by
    /// the no-access switch), narrowing would leave an empty page, so the list stays whole.
    /// </summary>
    public static bool IsNarrowed<T>(IEnumerable<T> teams, bool selectedOnly, bool showAll, string selectedKey, Func<T, string> keyOf)
        => selectedOnly
           && !showAll
           && !string.IsNullOrEmpty(selectedKey)
           && (teams ?? []).Any(team => keyOf(team) == selectedKey);

    /// <summary>The teams to show: all of them, or only the selected one when <see cref="IsNarrowed{T}"/>.</summary>
    public static T[] Apply<T>(IEnumerable<T> teams, bool selectedOnly, bool showAll, string selectedKey, Func<T, string> keyOf)
        => IsNarrowed(teams, selectedOnly, showAll, selectedKey, keyOf)
            ? [.. (teams ?? []).Where(team => keyOf(team) == selectedKey)]
            : [.. teams ?? []];

    /// <summary>
    /// Whether to offer the switch to all teams: the option is on and there is another team to switch to. A caller
    /// with one team has nothing to switch to, and a control that changes nothing is noise.
    /// </summary>
    public static bool ShowToggle<T>(IEnumerable<T> teams, bool selectedOnly, string selectedKey, Func<T, string> keyOf)
        => selectedOnly
           && !string.IsNullOrEmpty(selectedKey)
           && (teams ?? []).Any(team => keyOf(team) == selectedKey)
           && (teams ?? []).Any(team => keyOf(team) != selectedKey);
}
