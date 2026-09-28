using System.Security.Claims;
using Tharga.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp;

/// <summary>
/// <see cref="IMcpContext"/> implementation backed by a <see cref="ClaimsPrincipal"/>.
/// Reads UserId, TeamId, and the Developer role from standard Team claim types.
/// </summary>
/// <remarks>
/// A host provider reaches it through <see cref="McpContextExtensions.AsTeamContext"/>.
/// <para>
/// <b>Where each value comes from</b> — which is also how to build one in a unit test:
/// <list type="bullet">
/// <item><see cref="TeamId"/>: the <c>selectedTeamKey</c> argument; failing that, the principal's
/// <see cref="TeamClaimTypes.TeamKey"/> claim.</item>
/// <item><see cref="UserId"/>: the principal's <see cref="ClaimTypes.NameIdentifier"/> claim; failing that,
/// <c>sub</c>.</item>
/// <item><see cref="IsDeveloper"/>: the principal is in the role named by <c>developerRole</c>.</item>
/// </list>
/// </para>
/// <code>
/// var principal = new ClaimsPrincipal(new ClaimsIdentity(
/// [
///     new Claim(ClaimTypes.NameIdentifier, "user-1"),
///     new Claim(TeamClaimTypes.TeamKey, "team-1")
/// ], "test"));
///
/// IMcpContext context = new TeamMcpContext(principal, McpScope.Team, developerRole: "Developer");
/// </code>
/// </remarks>
public sealed class TeamMcpContext : IMcpContext
{
    /// <param name="principal">The authenticated user, or null for anonymous.</param>
    /// <param name="scope">The MCP scope this call belongs to.</param>
    /// <param name="developerRole">Role name that gates <see cref="McpScope.System"/> calls.</param>
    /// <param name="selectedTeamKey">
    /// Team named on this call, when one was. Replaces the team the caller is anchored to — it never adds
    /// to it, so a call addresses exactly one team either way.
    /// </param>
    /// <param name="selectedTeamScopes">
    /// The caller's effective scopes in <paramref name="selectedTeamKey"/>, already resolved. Supplied
    /// rather than derived from claims: the principal carries scope claims for the team it is *anchored*
    /// to, which is a different team, and reading those here is how a selection would silently grant the
    /// wrong team's access.
    /// </param>
    public TeamMcpContext(
        ClaimsPrincipal principal,
        McpScope scope,
        string developerRole,
        string selectedTeamKey = null,
        IReadOnlyList<string> selectedTeamScopes = null)
    {
        Scope = scope;
        UserId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? principal?.FindFirst("sub")?.Value;
        TeamId = selectedTeamKey ?? principal?.FindFirst(TeamClaimTypes.TeamKey)?.Value;
        IsDeveloper = principal?.IsInRole(developerRole) ?? false;
        SelectedTeamKey = selectedTeamKey;
        SelectedTeamScopes = selectedTeamScopes;
    }

    /// <summary>The team named on this call, or null when none was.</summary>
    internal string SelectedTeamKey { get; }

    /// <summary>
    /// Scopes held in <see cref="SelectedTeamKey"/>. Null when no team was named, which is what tells
    /// the scope checker to fall back to the caller's anchored team rather than refuse everything.
    /// </summary>
    internal IReadOnlyList<string> SelectedTeamScopes { get; }

    public string UserId { get; }
    public string TeamId { get; }
    public bool IsDeveloper { get; }
    public McpScope Scope { get; }
}
