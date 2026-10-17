namespace Tharga.Team;

/// <summary>
/// Default <see cref="ITenantRoleService"/>: merges code-registered roles (<see cref="ITenantRoleRegistry"/>)
/// with a team's runtime-defined custom roles (read via <see cref="ITeamService"/>) and unions their scopes
/// on top of the access-level and override scopes resolved by <see cref="IScopeRegistry"/>.
/// The role registry and scope registry are optional (null when the consumer configured neither).
/// </summary>
public class TenantRoleService : ITenantRoleService
{
    private readonly ITeamService _teamService;
    private readonly IScopeRegistry _scopeRegistry;
    private readonly ITenantRoleRegistry _roleRegistry;

    public TenantRoleService(ITeamService teamService, IScopeRegistry scopeRegistry = null, ITenantRoleRegistry roleRegistry = null)
    {
        _teamService = teamService;
        _scopeRegistry = scopeRegistry;
        _roleRegistry = roleRegistry;
    }

    public async Task<IReadOnlyList<TenantRoleDefinition>> GetRolesAsync(string teamKey)
    {
        var codeRoles = _roleRegistry?.All ?? [];
        var customRoles = await _teamService.GetTeamCustomRolesAsync(teamKey);

        var codeNames = codeRoles.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        return codeRoles
            .Concat(customRoles.Where(r => !codeNames.Contains(r.Name)))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetEffectiveScopesAsync(string teamKey, AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides = null)
    {
        // An unset level grants nothing; custom roles must not rescue it any more than code roles do.
        if (accessLevel is null) return [];

        var roleNameList = roleNames?.ToArray() ?? [];
        // Materialised once: scopeOverrides is passed to GetEffectiveScopes and
        // then enumerated again by the fallback, which would walk a lazy sequence
        // twice.
        var overrideList = scopeOverrides?.ToArray();

        var baseScopes = _scopeRegistry?.GetEffectiveScopes(accessLevel, roleNameList, overrideList)
                         ?? (overrideList?.Distinct().ToArray() ?? (IReadOnlyList<string>)[]);

        var customRoles = await _teamService.GetTeamCustomRolesAsync(teamKey);
        var customScopes = customRoles
            .Where(r => roleNameList.Contains(r.Name))
            .SelectMany(r => r.Scopes ?? []);

        return baseScopes.Union(customScopes).Distinct().ToList();
    }
}
