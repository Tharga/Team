namespace Tharga.Team;

/// <summary>
/// Team-aware view over tenant roles: composes the global code-registered roles
/// (<see cref="ITenantRoleRegistry"/>) with a team's runtime-defined custom roles, and resolves the
/// effective scopes for a member of a specific team. This is the resolution path that lets a member
/// assigned a custom role receive that role's scopes — the team-blind <see cref="ITenantRoleRegistry"/>
/// only knows code roles.
/// </summary>
public interface ITenantRoleService
{
    /// <summary>
    /// All roles applicable to the given team: code-registered roles plus the team's custom roles.
    /// Code roles take precedence on a name clash.
    /// </summary>
    Task<IReadOnlyList<TenantRoleDefinition>> GetRolesAsync(string teamKey);

    /// <summary>
    /// Effective scopes for a member of the given team: access-level scopes ∪ code-role scopes ∪
    /// custom-role scopes (for the role names the member holds) ∪ explicit scope overrides.
    /// <b>Empty for a null level</b>, roles and overrides included, as <see cref="IScopeRegistry.GetEffectiveScopes(AccessLevel?, IEnumerable{string}, IEnumerable{string})"/>.
    /// </summary>
    Task<IReadOnlyList<string>> GetEffectiveScopesAsync(string teamKey, AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides = null);

    /// <summary>
    /// <see cref="GetEffectiveScopesAsync(string, AccessLevel?, IEnumerable{string}, IEnumerable{string})"/> for a given
    /// kind of holder: for an API key, scopes registered for members only are not supplied by the level
    /// (Tharga/Team#412). Roles and overrides are added as named.
    /// </summary>
    /// <remarks>
    /// The default ignores the kind, so a host's own implementation keeps compiling and filters nothing;
    /// <see cref="TenantRoleService"/> does.
    /// </remarks>
    Task<IReadOnlyList<string>> GetEffectiveScopesAsync(string teamKey, AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides, PrincipalKind kind)
        => GetEffectiveScopesAsync(teamKey, accessLevel, roleNames, scopeOverrides);
}
