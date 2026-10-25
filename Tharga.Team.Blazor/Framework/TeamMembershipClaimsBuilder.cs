using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Computes the team-membership/consent claims for a caller in a given team: team key, the
/// <see cref="Roles.TeamMember"/> and <c>Team{AccessLevel}</c> roles, access level, member key, and the
/// effective team scopes. Shared by <see cref="TeamServerClaimsTransformation"/> (initial HTTP
/// enrichment) and <see cref="TeamClaimRevalidator"/> (periodic in-circuit revalidation) so the two
/// server paths cannot drift.
/// </summary>
/// <remarks>
/// Returns only the team-derived claims — not the team-independent system scopes (those come from the
/// caller's app roles and are added separately by the transformation). Returns an empty list when the
/// caller has no membership and no consent access to the team.
/// </remarks>
internal sealed class TeamMembershipClaimsBuilder
{
    private readonly IUserService _userService;
    private readonly ThargaBlazorOptions _options;
    private readonly TeamGrantResolver _resolver;

    public TeamMembershipClaimsBuilder(
        ITeamService teamService,
        IUserService userService,
        IOptions<ThargaBlazorOptions> options,
        IScopeRegistry scopeRegistry = null,
        ITenantRoleService tenantRoleService = null)
    {
        _userService = userService;
        _options = options.Value;
        _resolver = new TeamGrantResolver(teamService, scopeRegistry, tenantRoleService);
    }

    /// <param name="principal">The caller.</param>
    /// <param name="teamKey">The team to build the claims for.</param>
    /// <param name="recordActivity">
    /// Whether resolving the caller counts as them being active. True for a request; false for the periodic
    /// revalidation, which runs for an idle tab too.
    /// </param>
    public async Task<IReadOnlyList<Claim>> BuildAsync(ClaimsPrincipal principal, string teamKey, bool recordActivity = true)
    {
        var claims = new List<Claim>();
        if (string.IsNullOrEmpty(teamKey))
            return claims;

        var user = recordActivity
            ? await _userService.GetCurrentUserAsync(principal)
            : await _userService.GetCurrentUserAsync(principal, recordActivity: false);

        // Membership, suspension and consent are all decided by TeamGrantResolver, which the MCP surface
        // reads too. This method's job is only to express the answer as claims — two copies of the rule
        // is exactly how the team:read hole came about.
        var grant = await _resolver.ResolveAsync(principal, user?.Key, teamKey, _options.Consent.AccessLevel);

        // Null covers "not a member", "suspended", and "no consented role" alike, and no TeamKey claim is
        // issued for any of them: with one, service-layer checks would treat the caller as being in the
        // team regardless of holding no scopes there.
        if (grant == null)
            return claims;

        claims.Add(new Claim(TeamClaimTypes.TeamKey, teamKey));
        claims.Add(new Claim(ClaimTypes.Role, Roles.TeamMember));
        claims.Add(new Claim(ClaimTypes.Role, $"Team{grant.AccessLevel}"));
        claims.Add(new Claim(TeamClaimTypes.AccessLevel, grant.AccessLevel.ToString()));

        if (!string.IsNullOrEmpty(grant.MemberKey))
            claims.Add(new Claim(TeamClaimTypes.MemberKey, grant.MemberKey));

        foreach (var scope in grant.Scopes)
            claims.Add(new Claim(TeamClaimTypes.Scope, scope));

        return claims;
    }
}
