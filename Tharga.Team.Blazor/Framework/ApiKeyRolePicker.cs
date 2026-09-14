using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Resolves the tenant roles offered by an API key's role picker: the per-team merged set (code roles ∪ the
/// team's custom roles) when dynamic roles are enabled (<see cref="ITenantRoleService"/> registered), otherwise
/// the code-registered roles from <see cref="ITenantRoleRegistry"/>. Mirrors the member role picker so a custom
/// role can be assigned to a team API key exactly as it can to a member.
/// </summary>
internal static class ApiKeyRolePicker
{
    /// <summary>
    /// The roles to offer for the given team: the merged per-team set from <paramref name="tenantRoleService"/>
    /// when it and a non-empty <paramref name="teamKey"/> are present, otherwise the code-registered roles.
    /// </summary>
    internal static async Task<IReadOnlyList<TenantRoleDefinition>> ResolveAsync(
        ITenantRoleService tenantRoleService, ITenantRoleRegistry registry, string teamKey)
    {
        if (tenantRoleService != null && !string.IsNullOrEmpty(teamKey))
            return await tenantRoleService.GetRolesAsync(teamKey);
        return registry?.All ?? Array.Empty<TenantRoleDefinition>();
    }
}
