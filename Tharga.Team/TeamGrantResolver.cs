using System.Security.Claims;

// Namespace Tharga.Team rather than Tharga.Team.Service, which is where this used to live: both types are
// internal, so nothing outside can name them, and every existing call site sits in a namespace nested under
// this one and resolves them without a using.
namespace Tharga.Team;

/// <summary>
/// What one caller may do in one team: membership if they have it, consent if they do not, and nothing
/// if neither.
/// </summary>
/// <remarks>
/// Named a <i>grant</i> to keep it clear of <see cref="TeamAccess"/>, the unrelated ambient API for
/// declaring deliberate access from service code.
/// </remarks>
/// <param name="AccessLevel">The level the access is held at.</param>
/// <param name="Scopes">The effective team scopes at that level.</param>
/// <param name="MemberKey">The caller's member key, or null when the access comes from consent.</param>
/// <param name="IsMember">Whether the access comes from membership rather than consent.</param>
internal sealed record TeamGrant(
    AccessLevel AccessLevel,
    IReadOnlyList<string> Scopes,
    string MemberKey,
    bool IsMember);

/// <summary>
/// Resolves what a caller may do in a given team. <b>The single copy of that rule.</b>
/// </summary>
/// <remarks>
/// Both surfaces that answer this question read it from here: <c>TeamMembershipClaimsBuilder</c> turns
/// the result into claims for a Blazor circuit, and the MCP context accessor uses it to resolve a team
/// named on a call. They previously could not share it — the rule lived inside the Blazor builder — and
/// the toolkit has already paid for that shape once: the <c>team:read</c> hole existed because two
/// enforcement paths each carried their own copy and drifted apart.
/// <para>
/// The default consent level is a <i>parameter</i> rather than a read of
/// <c>ThargaBlazorOptions.Consent.AccessLevel</c>, because that type lives in the Blazor package and this
/// one is below it. Each caller supplies its own configured value.
/// </para>
/// </remarks>
internal sealed class TeamGrantResolver
{
    private readonly ITeamService _teamService;
    private readonly IScopeRegistry _scopeRegistry;
    private readonly ITenantRoleService _tenantRoleService;

    public TeamGrantResolver(
        ITeamService teamService,
        IScopeRegistry scopeRegistry = null,
        ITenantRoleService tenantRoleService = null)
    {
        _teamService = teamService;
        _scopeRegistry = scopeRegistry;
        _tenantRoleService = tenantRoleService;
    }

    /// <summary>
    /// Resolves <paramref name="userKey"/>'s access to <paramref name="teamKey"/>, or null when they have
    /// none — which is the same answer for "not a member", "suspended", and "no consented role".
    /// </summary>
    /// <remarks>
    /// A suspended member resolves to null rather than to an empty scope list, deliberately: callers use
    /// null to mean "grant nothing at all, and do not mark them as being in this team". Returning an
    /// access with no scopes would leave them looking like a member to anything that checks membership
    /// rather than scopes.
    /// </remarks>
    public async Task<TeamGrant> ResolveAsync(
        ClaimsPrincipal principal,
        string userKey,
        string teamKey,
        AccessLevel defaultConsentLevel)
    {
        if (string.IsNullOrEmpty(teamKey)) return null;

        var member = string.IsNullOrEmpty(userKey)
            ? null
            : await _teamService.GetTeamMemberAsync(teamKey, userKey);

        if (member != null) return await ResolveFromMemberAsync(teamKey, member);

        // Not a member — the caller may still reach the team through a global role it consented to.
        var roles = principal?.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray() ?? [];

        if (roles.Length == 0) return null;

        var consented = await _teamService.GetConsentedTeamsAsync(roles)
            .FirstOrDefaultAsync(t => t.Key == teamKey);

        if (consented == null) return null;

        var level = consented.ConsentAccessLevel ?? defaultConsentLevel;
        var consentScopes = _scopeRegistry?.GetEffectiveScopes(level, [], []) ?? [];

        return new TeamGrant(level, [.. consentScopes], MemberKey: null, IsMember: false);
    }

    /// <summary>
    /// The membership half of <see cref="ResolveAsync"/>, for a caller who already holds the member — the
    /// team-list filter reads it straight off the team it is deciding about. Null when
    /// <paramref name="member"/> is null or suspended, matching what <see cref="ResolveAsync"/> answers.
    /// </summary>
    /// <remarks>
    /// <b>Exists so the scope computation has one copy, not two.</b> The caller that has a member in hand
    /// would otherwise recompute effective scopes itself and drift — which is exactly how the gate on
    /// <c>ITeamManagementService</c>'s reads came to ignore tenant roles and consent alike
    /// (Tharga/Team#248). Refetching the member instead would answer correctly but turn one list into one
    /// query per team.
    /// </remarks>
    public async Task<TeamGrant> ResolveFromMemberAsync(string teamKey, ITeamMember member)
    {
        if (member == null || member.SuspendedAt != null) return null;

        var scopes = _tenantRoleService != null
            ? await _tenantRoleService.GetEffectiveScopesAsync(teamKey, member.AccessLevel, member.TenantRoles, member.ScopeOverrides)
            : _scopeRegistry?.GetEffectiveScopes(member.AccessLevel, member.TenantRoles, member.ScopeOverrides) ?? [];

        return new TeamGrant(member.AccessLevel, [.. scopes], member.Key, IsMember: true);
    }
}
