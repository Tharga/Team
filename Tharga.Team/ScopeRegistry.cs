namespace Tharga.Team;

/// <summary>
/// Stores scope definitions and resolves effective scopes for a given access level.
/// Owner and Administrator get all registered scopes.
/// User gets scopes registered at User or Viewer level.
/// Viewer gets only scopes registered at Viewer level.
/// Custom gets no base scopes at all (exempt from the Owner/Administrator all-scopes rule);
/// its effective scopes come solely from roles and scope overrides.
/// Role scopes are unioned with access level scopes.
/// </summary>
public class ScopeRegistry : IScopeRegistry
{
    private readonly List<ScopeDefinition> _scopes = new();
    private ITenantRoleRegistry _roleRegistry;

    public void SetRoleRegistry(ITenantRoleRegistry roleRegistry)
    {
        _roleRegistry = roleRegistry;
    }

    public IReadOnlyList<ScopeDefinition> All => _scopes;

    public void Register(string scopeName, AccessLevel defaultMinimumLevel, string description = null)
    {
        Add(new ScopeDefinition(scopeName, defaultMinimumLevel, description));
    }

    /// <summary>
    /// Registers a scope that no access level grants. The entry exists for documentation and validation:
    /// the scope appears in the catalogue with its description and can be validated against, but it is
    /// exempt from the Owner/Administrator all-scopes rule, rejected in tenant-defined custom roles, and
    /// not offered by the scope-override pickers.
    /// </summary>
    /// <remarks>
    /// Use this for a scope that should be held only as a recorded decision — one reaching regulated or
    /// classified records, say, where "a team administrator gets it automatically" is the wrong default.
    /// Grant it by naming it on a code-registered tenant role (<see cref="ITenantRoleRegistry"/>) or
    /// through an explicit scope override; enforcement via <see cref="RequireScopeAttribute"/> is
    /// unchanged, since the scope is checked from the claim rather than from this registry.
    /// <para>
    /// Do not attempt the same thing by registering at <see cref="AccessLevel.Custom"/>. That grants the
    /// scope to <i>every</i> level: Owner and Administrator take all registered scopes regardless of the
    /// declared minimum, and the fall-through filter <c>DefaultMinimumLevel &gt;= accessLevel</c> is
    /// satisfied by <c>Custom</c> for User and Viewer too.
    /// </para>
    /// </remarks>
    /// <param name="scopeName">The scope name, as checked by <see cref="RequireScopeAttribute"/>.</param>
    /// <param name="description">Human-readable description, shown in the scope catalogue.</param>
    public void RegisterGrantOnly(string scopeName, string description = null)
    {
        Add(new ScopeDefinition(scopeName, AccessLevel.Custom, description, GrantOnly: true));
    }

    private void Add(ScopeDefinition definition)
    {
        if (_scopes.Any(s => s.Name == definition.Name))
            throw new InvalidOperationException($"Scope '{definition.Name}' is already registered.");

        _scopes.Add(definition);
    }

    public IReadOnlyList<string> GetScopesForAccessLevel(AccessLevel accessLevel)
    {
        // Custom grants no base scopes — effective scopes come solely from roles and overrides.
        // Explicit guard so the invariant holds even if a scope is ever registered at Custom level.
        if (accessLevel == AccessLevel.Custom)
            return Array.Empty<string>();

        if (accessLevel <= AccessLevel.Administrator)
            return _scopes.Select(s => s.Name).ToList();

        return _scopes
            .Where(s => s.DefaultMinimumLevel >= accessLevel)
            .Select(s => s.Name)
            .ToList();
    }

    public IReadOnlyList<string> GetEffectiveScopes(AccessLevel accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides = null)
    {
        var accessLevelScopes = GetScopesForAccessLevel(accessLevel);

        var roleScopes = _roleRegistry != null && roleNames != null
            ? _roleRegistry.GetScopesForRoles(roleNames)
            : Array.Empty<string>();

        var overrides = scopeOverrides ?? Array.Empty<string>();

        return accessLevelScopes
            .Union(roleScopes)
            .Union(overrides)
            .Distinct()
            .ToList();
    }
}
