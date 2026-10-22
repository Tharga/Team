namespace Tharga.Team;

/// <summary>
/// Stores scope definitions and resolves effective scopes for a given access level.
/// Owner and Administrator get all registered scopes.
/// User gets scopes registered at User or Viewer level.
/// Viewer gets only scopes registered at Viewer level.
/// Custom gets no base scopes at all (exempt from the Owner/Administrator all-scopes rule);
/// its effective scopes come solely from roles and scope overrides.
/// Grant-only scopes (<see cref="RegisterGrantOnly"/>) are granted by no access level, Owner and
/// Administrator included; they too are held solely through roles and scope overrides.
/// Role scopes are unioned with access level scopes.
/// </summary>
public class ScopeRegistry : IScopeRegistry
{
    private readonly List<ScopeDefinition> _scopes = new();
    private readonly List<ScopeGroup> _groups = new();
    private ITenantRoleRegistry _roleRegistry;

    public void SetRoleRegistry(ITenantRoleRegistry roleRegistry)
    {
        _roleRegistry = roleRegistry;
    }

    public IReadOnlyList<ScopeDefinition> All => _scopes;

    public IReadOnlyList<ScopeGroup> Groups => _groups;

    /// <summary>
    /// Defines a group scopes can be shown under. Groups are shown in the order they are defined.
    /// </summary>
    /// <remarks>
    /// Display only. A scope naming a group that was never defined is still shown, under its key and after the
    /// defined groups, so a typo is visible rather than lost.
    /// </remarks>
    /// <param name="key">The key scopes name when registered with a group.</param>
    /// <param name="displayName">The heading, in English. Localised through the text provider under <see cref="ScopeGroup.TextKeyFor"/>.</param>
    public void DefineGroup(string key, string displayName)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A scope group needs a key.", nameof(key));

        if (_groups.Any(g => g.Key == key))
            throw new InvalidOperationException($"Scope group '{key}' is already defined.");

        _groups.Add(new ScopeGroup(key, string.IsNullOrWhiteSpace(displayName) ? key : displayName));
    }

    public void Register(string scopeName, AccessLevel defaultMinimumLevel, string description = null)
    {
        Add(new ScopeDefinition(scopeName, defaultMinimumLevel, description));
    }

    /// <summary>Registers a scope shown under <paramref name="group"/>. See <see cref="DefineGroup"/>.</summary>
    public void Register(string scopeName, AccessLevel defaultMinimumLevel, string description, string group)
    {
        Add(new ScopeDefinition(scopeName, defaultMinimumLevel, description, false, group));
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
        Add(new ScopeDefinition(scopeName, AccessLevel.Custom, description, true));
    }

    /// <summary>Registers a grant-only scope shown under <paramref name="group"/>. See <see cref="RegisterGrantOnly(string,string)"/>.</summary>
    public void RegisterGrantOnly(string scopeName, string description, string group)
    {
        Add(new ScopeDefinition(scopeName, AccessLevel.Custom, description, true, group));
    }

    private void Add(ScopeDefinition definition)
    {
        if (_scopes.Any(s => s.Name == definition.Name))
            throw new InvalidOperationException($"Scope '{definition.Name}' is already registered.");

        _scopes.Add(definition);
    }

    public IReadOnlyList<string> GetScopesForAccessLevel(AccessLevel? accessLevel)
    {
        // First, and explicit. The lifted comparisons below already fail closed for null, but by accident: an
        // accident that happens to be correct is not a guarantee, and before 4.0 this value was Owner.
        if (accessLevel is null)
            return Array.Empty<string>();

        // Custom grants no base scopes — effective scopes come solely from roles and overrides.
        // Explicit guard so the invariant holds even if a scope is ever registered at Custom level.
        if (accessLevel == AccessLevel.Custom)
            return Array.Empty<string>();

        // Filtered before both branches, not just the all-scopes one: a grant-only scope carries
        // DefaultMinimumLevel = Custom, which the fall-through comparison below would satisfy for User
        // and Viewer alike.
        var granted = _scopes.Where(s => !s.GrantOnly);

        if (accessLevel <= AccessLevel.Administrator)
            return granted.Select(s => s.Name).ToList();

        return granted
            .Where(s => s.DefaultMinimumLevel >= accessLevel)
            .Select(s => s.Name)
            .ToList();
    }

    public IReadOnlyList<string> GetEffectiveScopes(AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides = null)
    {
        if (accessLevel is null)
            return Array.Empty<string>();

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
