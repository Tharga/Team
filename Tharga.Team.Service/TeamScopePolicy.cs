using System.Security.Claims;

namespace Tharga.Team.Service;

/// <summary>
/// The single decision for "may this principal use this scope" — pure, principal-in, so every enforcement
/// path shares one implementation rather than restating the policy.
/// </summary>
/// <remarks>
/// Two callers with different shapes need the same answer: <see cref="TeamAuthorizer"/> resolves the
/// principal asynchronously (a Blazor circuit cannot be read synchronously) and delegates here, while the
/// enforcement proxies already hold a resolved principal and call it directly. Restating the policy in
/// both is how they came to disagree — the proxy checked that *a* team was selected and the scope claim
/// existed somewhere, which authorized acting on any team.
/// </remarks>
internal static class TeamScopePolicy
{
    /// <summary>
    /// Whether the caller holds <paramref name="scope"/> <b>for</b> <paramref name="teamKey"/>: the scope
    /// claim is present and the caller's <c>TeamKey</c> claim is that same team. In-team scopes are issued
    /// for the selected team only, so this is what confines them to it.
    /// </summary>
    public static bool HasTeamScope(ClaimsPrincipal principal, string scope, string teamKey)
    {
        if (principal == null || string.IsNullOrEmpty(teamKey)) return false;

        var callerTeam = principal.FindFirst(TeamClaimTypes.TeamKey)?.Value;
        if (string.IsNullOrEmpty(callerTeam) || callerTeam != teamKey) return false;

        return HasScopeClaim(principal, scope);
    }

    /// <summary>
    /// Whether the caller holds <paramref name="scope"/> for <paramref name="teamKey"/> <b>directly</b> — as a member
    /// of the team, or as a key issued for it — and not through the team's consent.
    /// </summary>
    /// <remarks>
    /// <b>For acts that decide consent itself.</b> A caller consented in at Administrator holds <c>team:manage</c>
    /// like a member does, and <see cref="HasTeamScope"/> cannot tell them apart. Letting that caller change the
    /// team's consent — or approve a request for it — would let the grant extend itself: remove its own expiry, raise
    /// its own level. The provenance is on the principal: <see cref="TeamClaimTypes.MemberKey"/> is issued only from
    /// a membership, and a team API key carries <see cref="TeamClaimTypes.ApiKeyId"/> without
    /// <see cref="TeamClaimTypes.IsSystemKey"/>. A system key acting through a team's consent has neither.
    /// </remarks>
    public static bool HasDirectTeamScope(ClaimsPrincipal principal, string scope, string teamKey)
    {
        if (!HasTeamScope(principal, scope, teamKey)) return false;

        var isMember = !string.IsNullOrEmpty(principal.FindFirst(TeamClaimTypes.MemberKey)?.Value);
        var isTeamKey = principal.HasClaim(c => c.Type == TeamClaimTypes.ApiKeyId)
                        && !principal.HasClaim(c => c.Type == TeamClaimTypes.IsSystemKey);

        return isMember || isTeamKey;
    }

    /// <summary>
    /// Whether the caller holds the system <paramref name="scope"/>. Authorizes across any team and
    /// requires no team to be selected — system scopes come from app roles or a system API key,
    /// independently of membership.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="TeamClaimTypes.SystemScope"/> only. A team-level grant of the same scope name does
    /// not satisfy this: an access level that happens to include <c>audit:read</c> must not authorize
    /// reading every team's audit log.
    /// </remarks>
    public static bool HasSystemScope(ClaimsPrincipal principal, string scope)
        => principal != null && HasClaim(principal, TeamClaimTypes.SystemScope, scope);

    private static bool HasScopeClaim(ClaimsPrincipal principal, string scope)
        => HasClaim(principal, TeamClaimTypes.Scope, scope);

    private static bool HasClaim(ClaimsPrincipal principal, string claimType, string scope)
        => principal.Claims.Any(c => c.Type == claimType && c.Value == scope);
}
