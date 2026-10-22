using Tharga.Team;

namespace Tharga.Team.Blazor.Features.Scopes;

/// <summary>One heading and the items under it. A null <see cref="Key"/> is the ungrouped items.</summary>
public sealed record ScopeSection<T>(string Key, string Heading, IReadOnlyList<T> Items);

/// <summary>
/// Orders and sections scopes by the groups a host defined. Pure, like <see cref="ScopeReference"/>, so the
/// rules are testable without rendering.
/// </summary>
public static class ScopeGrouping
{
    /// <summary>
    /// The group keys in display order: defined groups as defined, then any key a scope names without a
    /// definition, alphabetically. Ungrouped is not a key and is not in the result.
    /// </summary>
    public static IReadOnlyList<string> Order(IReadOnlyList<ScopeGroup> groups, IEnumerable<string> usedKeys)
    {
        var defined = (groups ?? []).Select(g => g.Key).ToList();
        var undefined = (usedKeys ?? [])
            .Where(k => !string.IsNullOrEmpty(k) && !defined.Contains(k))
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal);

        return [.. defined, .. undefined];
    }

    /// <summary>
    /// <paramref name="items"/> in group order, ungrouped last, keeping the incoming order inside each group.
    /// </summary>
    public static IReadOnlyList<T> OrderByGroup<T>(IEnumerable<T> items, Func<T, string> groupOf, IReadOnlyList<ScopeGroup> groups)
    {
        var list = items.ToList();
        var order = Order(groups, list.Select(groupOf));
        var rank = order.Select((key, index) => (key, index)).ToDictionary(x => x.key, x => x.index);

        return [.. list.OrderBy(item => groupOf(item) is { Length: > 0 } key && rank.TryGetValue(key, out var r) ? r : order.Count)];
    }

    /// <summary>
    /// The items split into one section per group that has any, in group order, then the ungrouped items. A
    /// single section with no heading when nothing is grouped, so a host without groups sees what it always saw.
    /// </summary>
    /// <param name="items">The items, already in the order they should keep inside a group.</param>
    /// <param name="groupOf">The group key of an item, or null when ungrouped.</param>
    /// <param name="groups">The host groups.</param>
    /// <param name="headingOf">The heading for a group key.</param>
    /// <param name="ungroupedHeading">The heading for the ungrouped items, used only when there are groups to tell them apart from.</param>
    public static IReadOnlyList<ScopeSection<T>> Sections<T>(
        IReadOnlyList<T> items, Func<T, string> groupOf, IReadOnlyList<ScopeGroup> groups, Func<string, string> headingOf, string ungroupedHeading)
    {
        var anyGrouped = items.Any(i => !string.IsNullOrEmpty(groupOf(i)));
        if (!anyGrouped) return [new ScopeSection<T>(null, null, items)];

        var sections = new List<ScopeSection<T>>();
        foreach (var key in Order(groups, items.Select(groupOf)))
        {
            var inGroup = items.Where(i => groupOf(i) == key).ToList();
            if (inGroup.Count > 0) sections.Add(new ScopeSection<T>(key, headingOf(key), inGroup));
        }

        var ungrouped = items.Where(i => string.IsNullOrEmpty(groupOf(i))).ToList();
        if (ungrouped.Count > 0) sections.Add(new ScopeSection<T>(null, ungroupedHeading, ungrouped));

        return sections;
    }
}
