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
    /// <b>Empty for a null level</b>, roles and overrides included, as <see cref="IScopeRegistry.GetEffectiveScopes"/>.
    /// </summary>
    Task<IReadOnlyList<string>> GetEffectiveScopesAsync(string teamKey, AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides = null);
}
