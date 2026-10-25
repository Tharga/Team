using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Re-evaluates a caller's team-membership/consent claims for the team they currently have selected and,
/// when they have changed since the circuit was established, produces a refreshed principal. Pure decision
/// logic (no timers, no framework auth-state plumbing) so it can be unit-tested directly;
/// <see cref="TeamRevalidatingAuthenticationStateProvider"/> drives it on an interval.
/// </summary>
/// <remarks>
/// Only team-derived claims are recomputed. Team-independent system scopes (granted by the caller's app
/// roles), the app roles themselves, and every other identity claim are preserved. Recompute failures are
/// swallowed (fail-open): the current claims are kept and the next interval retries, so a transient error
/// never signs a user out.
/// </remarks>
internal sealed class TeamClaimRevalidator
{
    private readonly TeamMembershipClaimsBuilder _membershipClaimsBuilder;
    private readonly ISystemRoleRegistry _systemRoleRegistry;
    private readonly ILogger<TeamClaimRevalidator> _logger;

    public TeamClaimRevalidator(
        TeamMembershipClaimsBuilder membershipClaimsBuilder,
        ISystemRoleRegistry systemRoleRegistry = null,
        ILogger<TeamClaimRevalidator> logger = null)
    {
        _membershipClaimsBuilder = membershipClaimsBuilder;
        _systemRoleRegistry = systemRoleRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Returns a refreshed principal when the caller's team claims have changed, or <c>null</c> when they
    /// are unchanged (or there is nothing team-scoped to revalidate, or the recompute failed).
    /// </summary>
    public async Task<ClaimsPrincipal> TryRefreshAsync(ClaimsPrincipal principal)
    {
        try
        {
            if (principal?.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
                return null;

            // The selected team travels on the principal as the team_id marker claim — no cookie or
            // HttpContext exists inside a live SignalR circuit.
            var teamKey = principal.FindFirst(Constants.TeamKeyCookie)?.Value;
            if (string.IsNullOrEmpty(teamKey))
                return null;

            var freshMembership = await _membershipClaimsBuilder.BuildAsync(principal, teamKey, recordActivity: false);

            // System scopes are not team-derived; they are kept regardless and excluded from the comparison.
            // Computed from the caller's app roles only (excluding the team roles enrichment added), mirroring
            // the transformation, which evaluates system scopes before any team role is present.
            var appRoles = principal.Claims
                .Where(c => c.Type == ClaimTypes.Role && !IsTeamRole(c.Value))
                .Select(c => c.Value)
                .ToArray();
            var systemScopes = _systemRoleRegistry?.GetScopesForRoles(appRoles)?.ToHashSet(StringComparer.Ordinal)
                               ?? new HashSet<string>(StringComparer.Ordinal);

            var newIdentity = new ClaimsIdentity(
                identity.Claims.Where(c => !IsMembershipClaim(c, systemScopes)),
                identity.AuthenticationType,
                identity.NameClaimType,
                identity.RoleClaimType);

            foreach (var claim in freshMembership)
            {
                if (!newIdentity.HasClaim(claim.Type, claim.Value))
                    newIdentity.AddClaim(new Claim(claim.Type, claim.Value));
            }

            var others = principal.Identities.Where(i => !ReferenceEquals(i, identity));
            var refreshed = new ClaimsPrincipal(new[] { newIdentity }.Concat(others));

            // The rebuild above restores every claim an access simulation removed, so it is re-applied
            // here. Without this a simulation would expire on its own at the next interval -- silently,
            // and up to ClaimRevalidation.Interval after the caller started it.
            AccessSimulationFilter.Apply(refreshed, ActiveSimulation(principal, teamKey));

            // Compared *after* filtering, and against the same objects the filter mutated. Comparing the
            // unfiltered fresh claims against the caller's filtered current ones would differ on every
            // pass, reporting a change each interval for as long as a simulation was active.
            var currentMembership = identity.Claims.Where(c => IsMembershipClaim(c, systemScopes));
            var refreshedMembership = newIdentity.Claims.Where(c => IsMembershipClaim(c, systemScopes));
            if (KeySet(currentMembership).SetEquals(KeySet(refreshedMembership)))
                return null;

            return refreshed;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Team claim revalidation failed; keeping current claims until the next interval.");
            return null;
        }
    }

    /// <summary>
    /// A claim added by team enrichment (as opposed to an app/identity claim): the team key, access level,
    /// member key, a team role (<see cref="Roles.TeamMember"/> or <c>Team{AccessLevel}</c>), or a team
    /// scope. Scope claims whose value is a system scope are excluded — those are team-independent.
    /// </summary>
    private static bool IsMembershipClaim(Claim claim, HashSet<string> systemScopes)
    {
        if (claim.Type is TeamClaimTypes.TeamKey or TeamClaimTypes.AccessLevel or TeamClaimTypes.MemberKey)
            return true;
        if (claim.Type == TeamClaimTypes.Scope)
            return !systemScopes.Contains(claim.Value);
        if (claim.Type == ClaimTypes.Role)
            return IsTeamRole(claim.Value);
        return false;
    }

    /// <summary>
    /// The simulation the caller currently has active, read from the marker claim the HTTP-side
    /// transformation stamped on.
    /// </summary>
    /// <remarks>
    /// There is no cookie and no <c>HttpContext</c> inside a live circuit, which is why the raw value
    /// travels on the principal. It stays raw rather than parsed so it is obviously untrusted wherever
    /// it is read.
    /// <para>
    /// Read for <paramref name="teamKey"/>, through the same check the transformation uses, so the two paths agree
    /// on whether a simulation is in force (Tharga/Team#276).
    /// </para>
    /// </remarks>
    private static AccessSimulation ActiveSimulation(ClaimsPrincipal principal, string teamKey)
        => AccessSimulationCookie.ReadForTeam(principal.FindFirst(AccessSimulationCookie.ClaimType)?.Value, teamKey);

    private static bool IsTeamRole(string value) => TeamRoleNames.IsTeamDerived(value);

    private static HashSet<string> KeySet(IEnumerable<Claim> claims)
        => claims.Select(c => c.Type + "\0" + c.Value).ToHashSet(StringComparer.Ordinal);
}
