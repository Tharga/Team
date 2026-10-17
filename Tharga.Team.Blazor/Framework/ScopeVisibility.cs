using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Applies <see cref="ITenantScopeVisibilityProvider"/> and <see cref="ITenantRoleVisibilityProvider"/>
/// to the scope and role pickers, and keeps a hidden grant from being lost when a picker saves.
/// </summary>
internal static class ScopeVisibility
{
    /// <summary>
    /// The scopes, among <paramref name="scopes"/>, that are hidden for <paramref name="teamKey"/>.
    /// Empty when there is no provider or no team: then nothing is hidden.
    /// </summary>
    internal static async Task<IReadOnlySet<string>> HiddenAsync(ITenantScopeVisibilityProvider provider, string teamKey, IEnumerable<string> scopes)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        if (provider == null || string.IsNullOrEmpty(teamKey)) return hidden;

        foreach (var scope in (scopes ?? []).Distinct(StringComparer.Ordinal))
        {
            if (!await provider.IsScopeVisibleAsync(teamKey, scope)) hidden.Add(scope);
        }

        return hidden;
    }

    /// <summary>
    /// The roles, among <paramref name="roles"/>, that are visible for <paramref name="teamKey"/>. All of
    /// them when there is no provider or no team.
    /// </summary>
    internal static async Task<IReadOnlyList<TenantRoleDefinition>> VisibleRolesAsync(ITenantRoleVisibilityProvider provider, string teamKey, IReadOnlyList<TenantRoleDefinition> roles)
    {
        if (provider == null || string.IsNullOrEmpty(teamKey) || roles == null || roles.Count == 0) return roles ?? [];

        var visible = new List<TenantRoleDefinition>(roles.Count);
        foreach (var role in roles)
        {
            if (await provider.IsRoleVisibleAsync(teamKey, role.Name)) visible.Add(role);
        }

        return visible;
    }

    /// <summary>
    /// The scope names a picker lists: grantable, inherited and already-held scopes, minus the hidden ones.
    /// </summary>
    internal static IReadOnlyList<string> Options(IEnumerable<string> grantable, IEnumerable<string> inherited, IEnumerable<string> overrides, IReadOnlySet<string> hidden)
        => (grantable ?? [])
            .Concat(inherited ?? [])
            .Concat(overrides ?? [])
            .Distinct(StringComparer.Ordinal)
            .Where(x => hidden == null || !hidden.Contains(x))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    /// <summary>The checked scopes a picker shows: inherited plus overrides, minus the hidden ones.</summary>
    internal static IReadOnlyList<string> Checked(IEnumerable<string> inherited, IEnumerable<string> overrides, IReadOnlySet<string> hidden)
        => (inherited ?? [])
            .Concat(overrides ?? [])
            .Distinct(StringComparer.Ordinal)
            .Where(x => hidden == null || !hidden.Contains(x))
            .ToList();

    /// <summary>
    /// The overrides to persist after the user changed a picker: the selected scopes that are not
    /// inherited, plus every override the principal already held that is hidden right now. The user
    /// could neither see nor remove those, so saving must not drop them.
    /// </summary>
    internal static string[] Persist(IEnumerable<string> selected, IEnumerable<string> inherited, IEnumerable<string> previousOverrides, IReadOnlySet<string> hidden)
    {
        var inheritedSet = new HashSet<string>(inherited ?? [], StringComparer.Ordinal);
        var keptHidden = (previousOverrides ?? []).Where(x => hidden != null && hidden.Contains(x));
        return (selected ?? [])
            .Where(x => !inheritedSet.Contains(x))
            .Concat(keptHidden)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
