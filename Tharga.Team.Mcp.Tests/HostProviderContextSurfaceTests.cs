using System.Reflection;
using System.Security.Claims;
using Tharga.Mcp;

namespace Tharga.Team.Mcp.Tests;

/// <summary>
/// A host's own provider can read the calling team and user through public API, and can build the context
/// it needs in a test.
/// </summary>
/// <remarks>
/// <b>Visibility is asserted by reflection, not by calling.</b> This assembly sees the bridge's internals, so
/// a call here compiles whether or not a host could make the same one.
/// </remarks>
public class HostProviderContextSurfaceTests
{
    [Fact]
    public void AsTeamContext_IsReachableFromOutsideTheAssembly()
    {
        var type = typeof(McpContextExtensions);
        var method = type.GetMethod(nameof(McpContextExtensions.AsTeamContext), BindingFlags.Public | BindingFlags.Static);

        Assert.True(type.IsPublic);
        Assert.NotNull(method);
    }

    /// <summary>The construction documented on <see cref="TeamMcpContext"/>, kept compiling and true.</summary>
    [Fact]
    public void TheDocumentedTestConstruction_YieldsTheTeamAndUser()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(TeamClaimTypes.TeamKey, "team-1")
        ], "test"));

        IMcpContext context = new TeamMcpContext(principal, McpScope.Team, developerRole: "Developer");

        Assert.Equal("team-1", context.AsTeamContext()?.TeamId);
        Assert.Equal("user-1", context.AsTeamContext()?.UserId);
        Assert.False(context.AsTeamContext()?.IsDeveloper);
    }

    [Fact]
    public void ASelectedTeam_WinsOverTheAnchoredOne()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.TeamKey, "anchored")], "test"));

        var context = new TeamMcpContext(principal, McpScope.Team, "Developer", selectedTeamKey: "selected");

        Assert.Equal("selected", context.TeamId);
    }

    [Fact]
    public void AForeignContext_YieldsNoIdentity()
    {
        var foreign = Substitute.For<IMcpContext>();

        Assert.Null(foreign.AsTeamContext());
    }
}
