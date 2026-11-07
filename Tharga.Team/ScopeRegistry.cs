namespace Tharga.Team;

/// <summary>
/// Stores scope definitions and resolves effective scopes for a given access level.
/// Owner and Administrator get all registered scopes.
/// User gets scopes registered at User or Viewer level.
/// Viewer gets only scopes registered at Viewer level.
/// Custom gets no base scopes at all (exempt from the Owner/Administrator all-scopes rule);
/// its effective scopes come solely from roles and scope overrides.
/// Grant-only scopes (<see cref="RegisterGrantOnly(string,string)"/>) are granted by no access level, Owner and
/// Administrator included; they too are held solely through roles and scope overrides.
/// Role scopes are unioned with access level scopes.
/// </summary>
public class ScopeRegistry : IScopeRegistry
{
    private readonly List<ScopeDefinition> _scopes = new();
    private readonly Dictionary<string, ScopeOverride> _overrides = new(StringComparer.Ordinal);
    private IReadOnlyList<ScopeDefinition> _effective;
    private readonly List<ScopeGroup> _groups = new();
    private ITenantRoleRegistry _roleRegistry;

    public void SetRoleRegistry(ITenantRoleRegistry roleRegistry)
    {
        _roleRegistry = roleRegistry;
    }

    /// <summary>Every registered scope, as the host's <see cref="Override"/> calls leave it.</summary>
    public IReadOnlyList<ScopeDefinition> All => _effective ??= [.. _scopes.Select(Apply)];

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
    /// Registers a scope shown under <paramref name="group"/> that the access level grants to <paramref name="appliesTo"/>
    /// only. <see cref="ScopePrincipals.Members"/> keeps it from API keys at the level (Tharga/Team#412).
    /// </summary>
    public void Register(string scopeName, AccessLevel defaultMinimumLevel, string description, string group, ScopePrincipals appliesTo)
    {
        Add(new ScopeDefinition(scopeName, defaultMinimumLevel, description, false, group, appliesTo));
    }

    /// <summary>Registers an ungrouped scope that the access level grants to <paramref name="appliesTo"/> only.</summary>
    public void Register(string scopeName, AccessLevel defaultMinimumLevel, string description, ScopePrincipals appliesTo)
    {
        Add(new ScopeDefinition(scopeName, defaultMinimumLevel, description, false, null, appliesTo));
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
        _effective = null;
    }

    /// <summary>
    /// Changes how a registered scope is granted, whoever registered it and whenever this is called
    /// (Tharga/Team#405). Only what is passed is changed.
    /// </summary>
    /// <remarks>
    /// <b>Order does not matter.</b> The override is kept by name and applied when the scope is read, not when it
    /// is registered, so it holds whether it is called before or after the module that registers the scope. That is
    /// what <see cref="Register(string,AccessLevel,string)"/> cannot offer a host: it throws on a name a module
    /// registered first. It wins over what the registering module chose, including a level that module took from its
    /// own options. A name no module registers fails at startup, by <c>ScopeOverrideCheck</c>.
    /// <para>
    /// Calling it again for the same scope adds to the earlier call; a value passed again replaces the earlier one.
    /// </para>
    /// </remarks>
    /// <param name="scopeName">The scope as registered, for example <c>apikey:manage</c>.</param>
    /// <param name="defaultMinimumLevel">The least-privileged level that is granted it automatically.</param>
    /// <param name="grantOnly">True to grant it to no access level, Owner and Administrator included; false to make a
    /// grant-only scope an ordinary one (give <paramref name="defaultMinimumLevel"/> with it).</param>
    /// <param name="group">The <see cref="ScopeGroup"/> key to show it under on the scope page.</param>
    /// <param name="appliesTo">Who the level grants it to; <see cref="ScopePrincipals.Members"/> keeps it from API keys.</param>
    /// <param name="description">The wording shown in the catalogue and pickers.</param>
    public void Override(string scopeName, AccessLevel? defaultMinimumLevel = null, bool? grantOnly = null, string group = null, ScopePrincipals? appliesTo = null, string description = null)
    {
        if (string.IsNullOrWhiteSpace(scopeName))
            throw new ArgumentException("A scope override needs the name of the scope.", nameof(scopeName));

        if (grantOnly == true && defaultMinimumLevel is { } level && level != AccessLevel.Custom)
            throw new ArgumentException($"'{scopeName}' cannot be both grant-only and given the level {level}: no level grants a grant-only scope.", nameof(grantOnly));

        _overrides[scopeName] = _overrides.TryGetValue(scopeName, out var earlier)
            ? new ScopeOverride(defaultMinimumLevel ?? earlier.Level, grantOnly ?? earlier.GrantOnly, group ?? earlier.Group, appliesTo ?? earlier.AppliesTo, description ?? earlier.Description)
            : new ScopeOverride(defaultMinimumLevel, grantOnly, group, appliesTo, description);
        _effective = null;
    }

    /// <summary>The names passed to <see cref="Override"/> that no module registered. Empty when every override has a scope.</summary>
    public IReadOnlyList<string> UnmatchedOverrides => [.. _overrides.Keys.Where(k => _scopes.All(s => s.Name != k)).Order(StringComparer.Ordinal)];

    private ScopeDefinition Apply(ScopeDefinition scope)
    {
        if (!_overrides.TryGetValue(scope.Name, out var o)) return scope;

        var grantOnly = o.GrantOnly ?? scope.GrantOnly;

        // A scope turned from grant-only to ordinary has no level of its own to fall back on.
        if (scope.GrantOnly && !grantOnly && o.Level is null)
            throw new InvalidOperationException($"Scope '{scope.Name}' is registered grant-only, so making it ordinary needs the level to grant it at.");

        return scope with
        {
            GrantOnly = grantOnly,
            DefaultMinimumLevel = grantOnly ? AccessLevel.Custom : o.Level ?? scope.DefaultMinimumLevel,
            Group = o.Group ?? scope.Group,
            AppliesTo = o.AppliesTo ?? scope.AppliesTo,
            Description = o.Description ?? scope.Description,
        };
    }

    private sealed record ScopeOverride(AccessLevel? Level, bool? GrantOnly, string Group, ScopePrincipals? AppliesTo, string Description);

    public IReadOnlyList<string> GetScopesForAccessLevel(AccessLevel? accessLevel)
        => GetScopesForAccessLevel(accessLevel, PrincipalKind.Member);

    public IReadOnlyList<string> GetScopesForAccessLevel(AccessLevel? accessLevel, PrincipalKind kind)
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
        // A members-only scope is not the level's to give an API key (Tharga/Team#412). Only the level's share is
        // cut: a role or an override naming it still reaches the key, because that is someone deciding.
        var granted = All.Where(s => !s.GrantOnly && (kind == PrincipalKind.Member || s.AppliesTo == ScopePrincipals.All));

        if (accessLevel <= AccessLevel.Administrator)
            return granted.Select(s => s.Name).ToList();

        return granted
            .Where(s => s.DefaultMinimumLevel >= accessLevel)
            .Select(s => s.Name)
            .ToList();
    }

    public IReadOnlyList<string> GetEffectiveScopes(AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides = null)
        => GetEffectiveScopes(accessLevel, roleNames, scopeOverrides, PrincipalKind.Member);

    public IReadOnlyList<string> GetEffectiveScopes(AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides, PrincipalKind kind)
    {
        if (accessLevel is null)
            return Array.Empty<string>();

        var accessLevelScopes = GetScopesForAccessLevel(accessLevel, kind);

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
