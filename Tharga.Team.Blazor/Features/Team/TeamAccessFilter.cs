namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// The team page's "hide teams I have no access to" switch: when it is offered, and what it leaves.
/// </summary>
/// <remarks>
/// Presentation only — it hides rows the caller could already see, and grants or withholds nothing.
/// Pure and static so it is unit-testable; the project has no bUnit, so a decision left in markup cannot be
/// tested. Mirrors <see cref="TeamVisibility"/>.
/// </remarks>
internal static class TeamAccessFilter
{
    /// <summary>
    /// Whether to offer the switch: only when some listed team gives the caller no access. A list where
    /// every team is reachable has nothing to hide, and a switch that changes nothing is noise.
    /// </summary>
    public static bool ShowToggle<T>(IEnumerable<T> teams, Func<T, AccessLevel?> accessOf)
        => (teams ?? []).Any(team => accessOf(team) == null);

    /// <summary>The teams to show: all of them, or with <paramref name="hideNoAccess"/> only those the caller can reach.</summary>
    public static T[] Apply<T>(IEnumerable<T> teams, bool hideNoAccess, Func<T, AccessLevel?> accessOf)
        => hideNoAccess
            ? [.. (teams ?? []).Where(team => accessOf(team) != null)]
            : [.. teams ?? []];
}
