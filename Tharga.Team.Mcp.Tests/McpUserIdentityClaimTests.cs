using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Mcp;
using Tharga.Team.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp.Tests;

public class McpUserIdentityClaimTests
{
    private const string Sub = "pairwise-sub";
    private const string Oid = "tenant-oid";

    private static ClaimsPrincipal EntraPrincipal(string oidClaimType)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Sub),
            new Claim(oidClaimType, Oid)
        ], "TestAuth"));

    private static TeamMcpContext Current(ClaimsPrincipal principal, UserIdentityResolver registered)
    {
        var services = new ServiceCollection();
        if (registered != null) services.AddSingleton(registered);

        var httpContext = new DefaultHttpContext { User = principal, RequestServices = services.BuildServiceProvider() };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        return new HttpContextMcpContextAccessor(accessor, Options.Create(new McpTeamOptions())).Current.AsTeamContext();
    }

    [Fact]
    public void NoResolverRegistered_UserIdIsNameIdentifier()
    {
        Assert.Equal(Sub, Current(EntraPrincipal(DirectoryClaimTypes.ObjectIdentifier), null).UserId);
    }

    [Fact]
    public void DefaultResolverRegistered_UserIdIsNameIdentifier()
    {
        Assert.Equal(Sub, Current(EntraPrincipal(DirectoryClaimTypes.ObjectIdentifier), UserIdentityResolver.Default).UserId);
    }

    [Theory]
    [InlineData(DirectoryClaimTypes.ObjectId)]
    [InlineData(DirectoryClaimTypes.ObjectIdentifier)]
    public void ConfiguredOid_UserIdIsOid(string oidClaimType)
    {
        var resolver = new UserIdentityResolver([DirectoryClaimTypes.ObjectId]);

        Assert.Equal(Oid, Current(EntraPrincipal(oidClaimType), resolver).UserId);
    }

    [Fact]
    public void PublicConstructor_KeepsTheDefault()
    {
        var ctx = new TeamMcpContext(EntraPrincipal(DirectoryClaimTypes.ObjectId), McpScope.User, "Developer");

        Assert.Equal(Sub, ctx.UserId);
    }
}
