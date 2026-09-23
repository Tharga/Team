using Tharga.Team;

namespace Tharga.Team.Blazor.Features.Scopes;

/// <summary>
/// One row of the scope reference: a configured scope, its description, and who grants it
/// (the access levels and tenant roles).
/// </summary>
/// <param name="Name">The scope name.</param>
/// <param name="Description">Human-readable description, or null when none was registered.</param>
/// <param name="AccessLevels">The access levels that grant this scope. Always empty for a grant-only scope.</param>
/// <param name="Roles">The tenant roles that name this scope.</param>
/// <param name="GrantOnly">
/// True when the scope is registered grant-only. Such a row exists precisely so the scope is documented:
/// it is granted by no access level, so without the flag it would read as an ordinary scope that simply
/// nobody happens to have.
/// </param>
public sealed record ScopeRow(
    string Name,
    string Description,
    IReadOnlyList<AccessLevel> AccessLevels,
    IReadOnlyList<string> Roles,
    bool GrantOnly = false);

/// <summary>
/// How a single scope is granted under the current selection: by the selected access level, by one or
/// more of the selected roles, and/or by the user's personal scope overrides. <see cref="Granted"/> is the
/// union — true when the scope is granted by any source.
/// </summary>
public sealed record ScopeGrant(bool Granted, bool ByLevel, IReadOnlyList<string> ByRoles, bool ByOverride);

/// <summary>
/// Builds the read-only scope reference shown by <c>ScopeView</c> directly from the registries, so it
/// always reflects the live configuration. Pure (no DI, no rendering) to keep it unit-testable.
/// </summary>
public static class ScopeReference
{
    // Access levels that grant scopes by default. Custom grants nothing by default and is omitted.
    private static readonly AccessLevel[] Levels =
        [AccessLevel.Owner, AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer];

    /// <summary>
    /// Projects every registered scope to a <see cref="ScopeRow"/>, resolving which access levels and
    /// tenant roles grant it. Returns an empty list when no scope registry is configured.
    /// </summary>
    /// <param name="scopes">The configured scope registry, or null when scopes are not configured.</param>
    /// <param name="roles">The configured tenant role registry, or null when roles are not configured.</param>
    /// <remarks>
    /// <b>Sees code-registered roles only.</b> <see cref="ITenantRoleRegistry"/> is team-blind, so a team's
    /// own custom roles are absent from the rows this produces. Prefer <see cref="BuildForRoles"/> with a
    /// list resolved for the team — building from the registry alone is what made <c>ScopeView</c> tell a
    /// member they lacked scopes a custom role had granted them (Tharga/Team#292).
    /// </remarks>
    public static IReadOnlyList<ScopeRow> Build(IScopeRegistry scopes, ITenantRoleRegistry roles)
        => BuildForRoles(scopes, roles?.All);

    /// <summary>
    /// Projects every registered scope to a <see cref="ScopeRow"/> against an explicit role list — the
    /// team's roles, merged from code and its own definitions.
    /// </summary>
    /// <param name="scopes">The configured scope registry, or null when scopes are not configured.</param>
    /// <param name="roles">
    /// The roles to credit, usually from <c>ITenantRoleService.GetRolesAsync(teamKey)</c>. Null or empty
    /// credits nothing, which is not an error — it is what a team with no roles looks like.
    /// </param>
    /// <remarks>
    /// <b>Named rather than overloaded on purpose.</b> A second <c>Build</c> taking a list would make the
    /// existing <c>Build(scopes, null)</c> ambiguous — a compile error in every caller passing a bare null,
    /// consumers included. A fix for a display defect should not cost anyone a cast.
    /// </remarks>
    public static IReadOnlyList<ScopeRow> BuildForRoles(IScopeRegistry scopes, IReadOnlyList<TenantRoleDefinition> roles)
    {
        if (scopes == null) return Array.Empty<ScopeRow>();

        // Resolve level -> scope-set via the registry's own logic rather than reimplementing the math.
        var byLevel = Levels.ToDictionary(l => l, l => new HashSet<string>(scopes.GetScopesForAccessLevel(l)));
        var roleList = roles ?? Array.Empty<TenantRoleDefinition>();

        return scopes.All
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new ScopeRow(
                s.Name,
                s.Description,
                Levels.Where(l => byLevel[l].Contains(s.Name)).ToList(),
                roleList.Where(r => r.Scopes != null && r.Scopes.Contains(s.Name)).Select(r => r.Name).ToList(),
                s.GrantOnly))
            .ToList();
    }

    /// <summary>
    /// The member's own role assignments, narrowed to those the page knows about — what the role bar shows
    /// as already selected.
    /// </summary>
    /// <param name="memberRoles">The member's assigned role names, from their team member record.</param>
    /// <param name="known">The roles resolved for the team, from <see cref="BuildForRoles"/>'s own list.</param>
    /// <remarks>
    /// <b>Pure so it can be tested; the narrowing is where the defect lived.</b> Filtering against the
    /// code-registered roles alone dropped every custom role the member held, and the scopes those roles
    /// granted then rendered as not held — on the page people open to find out what they hold
    /// (Tharga/Team#292). An assignment naming a role the team no longer defines is still dropped, which is
    /// correct: there is nothing to credit it with.
    /// </remarks>
    public static IReadOnlyList<string> PreselectedRoles(IEnumerable<string> memberRoles, IReadOnlyList<TenantRoleDefinition> known)
    {
        var names = new HashSet<string>((known ?? Array.Empty<TenantRoleDefinition>()).Select(r => r.Name), StringComparer.Ordinal);
        return (memberRoles ?? Array.Empty<string>()).Where(names.Contains).ToList();
    }

    /// <summary>
    /// Returns the system scopes the current principal actually holds: the registered system scopes
    /// (<paramref name="system"/>) whose name appears in the principal's granted <paramref name="userScopes"/>
    /// (its <c>Scope</c> claims). Not a full catalog — only what the user has. Ordered by name.
    /// </summary>
    public static IReadOnlyList<SystemScopeDefinition> UserSystemScopes(ISystemScopeRegistry system, IEnumerable<string> userScopes)
    {
        if (system == null) return Array.Empty<SystemScopeDefinition>();

        var held = userScopes as IReadOnlySet<string> ?? new HashSet<string>(userScopes ?? Enumerable.Empty<string>());

        return system.All
            .Where(s => held.Contains(s.Name))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Maps an actual access level to the value selectable in the access-level bar: Owner collapses to
    /// Administrator (they grant identical scopes), and Custom maps to null (no base scopes — its effective
    /// scopes come solely from roles and overrides).
    /// </summary>
    public static AccessLevel? ToSelectableLevel(AccessLevel actual) => actual switch
    {
        AccessLevel.Owner => AccessLevel.Administrator,
        AccessLevel.Custom => null,
        _ => actual,
    };

    /// <summary>
    /// Resolves how <paramref name="row"/> is granted under the current selection: by the selected access
    /// <paramref name="level"/>, by any of the <paramref name="selectedRoles"/>, and/or by the user's
    /// personal scope <paramref name="overrides"/>.
    /// </summary>
    public static ScopeGrant Resolve(ScopeRow row, AccessLevel? level, IReadOnlyCollection<string> selectedRoles, IReadOnlyCollection<string> overrides)
    {
        var byLevel = level.HasValue && row.AccessLevels.Contains(level.Value);
        var byRoles = selectedRoles == null
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : row.Roles.Where(selectedRoles.Contains).ToList();
        var byOverride = overrides != null && overrides.Contains(row.Name);
        return new ScopeGrant(byLevel || byRoles.Count > 0 || byOverride, byLevel, byRoles, byOverride);
    }
}
