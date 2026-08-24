using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Service-layer authorization primitives for team operations, read from the caller's claims via
/// <see cref="ITeamPrincipalAccessor"/> (so they work for HTTP/API callers and interactive Blazor circuits
/// alike). The authorization decorator over <c>ITeamService</c> composes these per operation:
/// <list type="bullet">
/// <item><b>In-team scopes</b> (<see cref="TeamScopes.Manage"/>, <see cref="TeamScopes.MemberManage"/>, …)
/// authorize only the caller's <i>own</i> team — the <c>TeamKey</c> claim must equal the target
/// <c>teamKey</c>, closing the "admin of team A acts on team B" hole.</item>
/// <item><b>System scopes</b> (<see cref="SystemTeamScopes.Delete"/>) authorize across <i>any</i> team —
/// no team binding.</item>
/// </list>
/// Claims are the source of truth: scope claims are emitted from the caller's access level / roles /
/// overrides for their team (or from a system key's scope list), so a present scope claim already reflects
/// the underlying membership.
/// </summary>
public sealed class TeamAuthorizer
{
    private readonly ITeamPrincipalAccessor _principalAccessor;

    public TeamAuthorizer(ITeamPrincipalAccessor principalAccessor)
    {
        _principalAccessor = principalAccessor;
    }

    /// <summary>True when there is an authenticated caller (any identity).</summary>
    public async ValueTask<bool> IsAuthenticatedAsync()
    {
        var principal = await _principalAccessor.GetCurrentAsync();
        return principal?.Identity?.IsAuthenticated ?? false;
    }

    /// <summary>
    /// True when the caller holds <paramref name="scope"/> <b>for</b> <paramref name="teamKey"/>: the scope
    /// claim is present <b>and</b> the caller's <c>TeamKey</c> claim equals <paramref name="teamKey"/>. The
    /// scope only authorizes the caller's own team.
    /// </summary>
    public async ValueTask<bool> HasTeamScopeAsync(string scope, string teamKey)
    {
        var principal = await _principalAccessor.GetCurrentAsync();
        return TeamScopePolicy.HasTeamScope(principal, scope, teamKey);
    }

    /// <summary>True when the caller holds the system <paramref name="scope"/> (authorizes any team; no team binding).</summary>
    public async ValueTask<bool> HasSystemScopeAsync(string scope)
    {
        var principal = await _principalAccessor.GetCurrentAsync();
        return TeamScopePolicy.HasSystemScope(principal, scope);
    }

    /// <summary>
    /// True when the caller's <c>TeamKey</c> claim equals <paramref name="teamKey"/> — membership, with no
    /// scope required.
    /// </summary>
    /// <remarks>
    /// <b>For the operations a member may perform on their own behalf.</b> Raising a support case about your
    /// own team is one: gating it behind a scope would mean every host granting that scope to everybody, and
    /// a scope everyone holds checks nothing. <c>shared-instructions.md</c> makes the general point — an
    /// entry point's check need not be a scope, only a check; the invitation path is the other example.
    /// <para>
    /// Still a real boundary: a caller with no team selected, or one whose selected team is a different
    /// tenant, fails it.
    /// </para>
    /// </remarks>
    public async ValueTask<bool> IsMemberOfAsync(string teamKey)
    {
        var principal = await _principalAccessor.GetCurrentAsync();

        if (!(principal?.Identity?.IsAuthenticated ?? false)) return false;

        var claim = principal.FindFirst(TeamClaimTypes.TeamKey)?.Value;

        return !string.IsNullOrEmpty(claim) && claim == teamKey;
    }

    /// <summary>
    /// The caller's stable authentication subject, or <c>null</c> when unauthenticated.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately the same value the audit trail records as <c>CallerUserIdentity</c></b> —
    /// <see cref="ClaimTypes.NameIdentifier"/>, with no fallback chain. Anything that identifies a person
    /// durably has to agree with the audit trail, or two records of the same act cannot be joined. A
    /// per-team <c>MemberKey</c> would be wrong here for a second reason: it stops resolving when someone
    /// leaves the team, and the things keyed on this outlive membership.
    /// </remarks>
    public async ValueTask<string> GetSubjectAsync()
    {
        var principal = await _principalAccessor.GetCurrentAsync();

        return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// The caller's display name, for snapshotting into durable history. Falls back through the usual
    /// chain and finally to the subject, so it is never empty for an authenticated caller.
    /// </summary>
    public async ValueTask<string> GetDisplayNameAsync()
    {
        var principal = await _principalAccessor.GetCurrentAsync();
        if (principal == null) return null;

        return principal.FindFirst(ClaimTypes.Name)?.Value
               ?? principal.Identity?.Name
               ?? principal.FindFirst(ClaimTypes.Email)?.Value
               ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
