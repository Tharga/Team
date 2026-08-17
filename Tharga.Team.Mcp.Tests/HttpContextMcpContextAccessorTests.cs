using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Mcp;
using Tharga.Team.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp.Tests;

public class HttpContextMcpContextAccessorTests
{
    [Fact]
    public void Current_Null_WhenNoHttpContext()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext)null);
        var sut = new HttpContextMcpContextAccessor(accessor, Options.Create(new McpTeamOptions()));

        Assert.Null(sut.Current);
    }

    [Fact]
    public void Current_ExposesClaims()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(TeamClaimTypes.TeamKey, "team-1"),
            new Claim(ClaimTypes.Role, "Developer"),
        }, "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpContextMcpContextAccessor(accessor, Options.Create(new McpTeamOptions()));

        var ctx = sut.Current.AsTeamContext();

        Assert.NotNull(ctx);
        Assert.Equal("user-1", ctx.UserId);
        Assert.Equal("team-1", ctx.TeamId);
        Assert.True(ctx.IsDeveloper);
    }

    [Fact]
    public void Setter_IsNoOp()
    {
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("TestAuth")) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpContextMcpContextAccessor(accessor, Options.Create(new McpTeamOptions()));

        // Setter should not throw even though it's a no-op
        sut.Current = null;
    }

    [Fact]
    public void Scope_Is_System_When_Caller_Has_Developer_Role()
    {
        var ctx = CreateContext(new Claim(ClaimTypes.Role, "Developer"));

        Assert.Equal(McpScope.System, ctx.Scope);
        Assert.True(ctx.IsDeveloper);
    }

    [Fact]
    public void Scope_Is_System_When_Caller_Has_IsSystemKey_Claim()
    {
        var ctx = CreateContext(new Claim(TeamClaimTypes.IsSystemKey, "true"));

        Assert.Equal(McpScope.System, ctx.Scope);
    }

    [Fact]
    public void Scope_Is_Team_When_Caller_Has_TeamKey_But_No_Developer_Role()
    {
        var ctx = CreateContext(new Claim(TeamClaimTypes.TeamKey, "team-1"));

        Assert.Equal(McpScope.Team, ctx.Scope);
        Assert.False(ctx.IsDeveloper);
    }

    [Fact]
    public void Scope_Is_User_When_Caller_Has_Neither_Developer_Nor_Team()
    {
        var ctx = CreateContext(new Claim(ClaimTypes.NameIdentifier, "user-1"));

        Assert.Equal(McpScope.User, ctx.Scope);
    }

    [Fact]
    public void DeveloperRole_Takes_Precedence_Over_TeamKey()
    {
        var ctx = CreateContext(
            new Claim(TeamClaimTypes.TeamKey, "team-1"),
            new Claim(ClaimTypes.Role, "Developer"));

        Assert.Equal(McpScope.System, ctx.Scope);
    }

    [Fact]
    public void Custom_DeveloperRole_Is_Honored()
    {
        var options = Options.Create(new McpTeamOptions { DeveloperRole = "SuperAdmin" });
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "SuperAdmin") }, "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpContextMcpContextAccessor(accessor, options);

        Assert.Equal(McpScope.System, sut.Current.Scope);
    }

    private static TeamMcpContext CreateContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        var sut = new HttpContextMcpContextAccessor(accessor, Options.Create(new McpTeamOptions()));
        return sut.Current.AsTeamContext();
    }
}
