using System.Security.Claims;
using Tharga.Mcp;
using Tharga.Team.Mcp;

namespace Tharga.Team.Mcp.Tests;

/// <summary>
/// Builds a real <see cref="TeamMcpContext"/> for tests.
/// </summary>
/// <remarks>
/// These tests used to substitute <see cref="IMcpContext"/> and stub <c>UserId</c> / <c>TeamId</c> /
/// <c>IsDeveloper</c> on it. From `Tharga.Mcp` 2.0.0 the interface carries only <see cref="McpScope"/>, and
/// those three live on this bridge's own <see cref="TeamMcpContext"/>, which derives them from the
/// <see cref="ClaimsPrincipal"/>. A substitute of the interface would therefore satisfy the compiler and
/// still fail every provider, because <c>AsTeamContext()</c> would return null and the provider would see
/// no caller at all.
/// <para>
/// Building the real thing from claims is also the stronger test: it exercises the claim types the accessor
/// actually reads, so a change to <see cref="TeamClaimTypes.TeamKey"/> or to the role handling now fails
/// here instead of passing against a stub.
/// </para>
/// </remarks>
internal static class TestMcpContextFactory
{
    internal const string DeveloperRole = "Developer";

    internal static TeamMcpContext Create(
        string userId = null,
        string teamId = null,
        bool isDeveloper = false,
        McpScope scope = McpScope.Team,
        string selectedTeamKey = null,
        IReadOnlyList<string> selectedTeamScopes = null)
    {
        var claims = new List<Claim>();
        if (userId != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (teamId != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, teamId));
        if (isDeveloper) claims.Add(new Claim(ClaimTypes.Role, DeveloperRole));

        // The role claim type must be named explicitly, or IsInRole never matches and IsDeveloper is
        // always false - which would make an authorization test pass for the wrong reason.
        var identity = new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.Role);

        return new TeamMcpContext(new ClaimsPrincipal(identity), scope, DeveloperRole, selectedTeamKey, selectedTeamScopes);
    }
}
