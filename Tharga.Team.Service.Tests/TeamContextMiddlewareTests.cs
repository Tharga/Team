using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The header turns into <b>claims</b>, which is what makes every existing check work unchanged.
/// </summary>
/// <remarks>
/// The alternative was an ambient "current team" value that each endpoint reads. That would have needed
/// every authorization path — <c>ScopeProxy</c>, <c>[RequireScope]</c>, <c>TeamScopePolicy</c> — taught
/// about it, and a host's own controllers would have been left out entirely. Claims are the thing they
/// all already read, so the mechanism disappears at the point of use.
/// </remarks>
public class TeamContextMiddlewareTests
{
    private const string Header = "X-Team-Key";
    private const string OwnTeam = "team-1";
    private const string OtherTeam = "team-2";

    private sealed record FakeTeam(string Key, string[] ConsentedRoles, AccessLevel? ConsentAccessLevel) : ITeam
    {
        public string Name => Key;
        public string Icon => null;
    }

    private static async Task<(HttpContext Context, bool Continued)> RunAsync(
        ClaimsPrincipal principal, string headerValue, ITeam consentingTeam = null)
    {
        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamByKeyAsync(Arg.Any<string>()).Returns((ITeam)null);
        if (consentingTeam != null) teamService.GetTeamByKeyAsync(consentingTeam.Key).Returns(consentingTeam);

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(["team:read", "audit:read"]);
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>(), PrincipalKind.ApiKey)
            .Returns(["team:read", "audit:read"]);

        var context = new DefaultHttpContext { User = principal };
        context.Response.Body = new MemoryStream();
        if (headerValue != null) context.Request.Headers[Header] = headerValue;

        var continued = false;
        var sut = new TeamContextMiddleware(_ => { continued = true; return Task.CompletedTask; },
            Options.Create(new TeamContextOptions()));

        await sut.InvokeAsync(context, new TeamContextResolver(teamService, registry));

        return (context, continued);
    }

    private static ClaimsPrincipal TeamKey(string teamKey)
        => new(new ClaimsIdentity([new Claim(TeamClaimTypes.TeamKey, teamKey)], "Test"));

    private static ClaimsPrincipal SystemKey()
        => new(new ClaimsIdentity([new Claim(TeamClaimTypes.IsSystemKey, "true")], "Test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    /// <summary>
    /// The whole point: after the middleware, a system key naming a consenting team looks to every
    /// downstream check exactly like a caller holding those scopes on that team.
    /// </summary>
    [Fact]
    public async Task ASystemKeyNamingAConsentingTeam_GainsTheTeamsClaims()
    {
        var (context, continued) = await RunAsync(
            SystemKey(), OtherTeam, new FakeTeam(OtherTeam, ["Support"], AccessLevel.User));

        Assert.True(continued);
        Assert.Equal(OtherTeam, context.User.FindFirst(TeamClaimTypes.TeamKey)?.Value);
        Assert.Contains(context.User.Claims, c => c.Type == TeamClaimTypes.Scope && c.Value == "audit:read");
    }

    /// <summary>
    /// And therefore satisfies the ordinary team-scope check, with nothing taught about headers. This is
    /// the claim that a host's own controllers are covered for free.
    /// </summary>
    [Fact]
    public async Task TheResultingPrincipal_SatisfiesAnOrdinaryTeamScopeCheck()
    {
        var (context, _) = await RunAsync(
            SystemKey(), OtherTeam, new FakeTeam(OtherTeam, ["Support"], AccessLevel.User));

        Assert.True(TeamScopePolicy.HasTeamScope(context.User, "audit:read", OtherTeam));
        Assert.False(TeamScopePolicy.HasTeamScope(context.User, "audit:read", "some-other-team"));
    }

    [Fact]
    public async Task ASystemKeyNamingANonConsentingTeam_IsForbiddenAndStopsThere()
    {
        var (context, continued) = await RunAsync(
            SystemKey(), OtherTeam, new FakeTeam(OtherTeam, [], AccessLevel.Administrator));

        Assert.False(continued);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task ATeamKeyNamingAnotherTeam_IsForbiddenAndStopsThere()
    {
        var (context, continued) = await RunAsync(TeamKey(OwnTeam), OtherTeam);

        Assert.False(continued);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    /// <summary>
    /// A team key gains nothing here. Its scopes are already on its claims, and re-deriving them would be
    /// a second, quieter place deciding what a team key may do.
    /// </summary>
    [Fact]
    public async Task ATeamKey_GainsNoExtraClaims()
    {
        var (context, continued) = await RunAsync(TeamKey(OwnTeam), headerValue: null);

        Assert.True(continued);
        Assert.Single(context.User.Claims, c => c.Type == TeamClaimTypes.TeamKey);
        Assert.DoesNotContain(context.User.Claims, c => c.Type == TeamClaimTypes.Scope);
    }

    [Fact]
    public async Task ASystemKeyWithNoHeader_PassesThroughUnchanged()
    {
        var (context, continued) = await RunAsync(SystemKey(), headerValue: null);

        Assert.True(continued);
        Assert.DoesNotContain(context.User.Claims, c => c.Type == TeamClaimTypes.TeamKey);
    }

    /// <summary>An unauthenticated request is not the middleware's business; authentication refuses it.</summary>
    [Fact]
    public async Task AnAnonymousRequest_PassesThroughUntouched()
    {
        var (context, continued) = await RunAsync(Anonymous(), OtherTeam);

        Assert.True(continued);
        Assert.DoesNotContain(context.User.Claims, c => c.Type == TeamClaimTypes.TeamKey);
    }

    /// <summary>A blank header is no header, not a request to act on nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankHeader_IsTreatedAsAbsent(string value)
    {
        var (context, continued) = await RunAsync(SystemKey(), value);

        Assert.True(continued);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
