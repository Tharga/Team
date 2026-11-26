using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Blazor.Framework;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What <c>/logout</c> does about the server-side session (Tharga/Team#471): it ends the caller's session as signed out when sessions are active, and does
/// nothing new otherwise. Beside <c>ThargaAuthEndpointBehaviourTests</c>, whose guarantees (both schemes signed out, the provider last, its redirect left intact)
/// must hold with sessions on, because ending the session must never cost the sign-out.
/// </summary>
public class SessionLogoutTests
{
    private const string EndSessionUrl = "https://test.ciamlogin.com/test/oauth2/v2.0/logout?post_logout_redirect_uri=%2F";

    private const string ValidAzureAdConfig = """
        {
            "AzureAd": {
                "Authority": "https://test.ciamlogin.com/test",
                "ClientId": "test-client-id",
                "TenantId": "test-tenant-id",
                "CallbackPath": "/signin-oidc"
            }
        }
        """;

    private readonly Mock<IUserSessionService> _sessions = new();
    private readonly List<string> _order = [];

    private sealed class RecordingAuthenticationService(List<string> order) : IAuthenticationService
    {
        public List<string> SignOuts { get; } = [];

        /// <summary>The properties each scheme was signed out with; a federated sign-out's <c>RedirectUri</c> is where the provider sends the browser back to.</summary>
        public Dictionary<string, AuthenticationProperties> Properties { get; } = [];

        public Task SignOutAsync(HttpContext context, string scheme, AuthenticationProperties properties)
        {
            SignOuts.Add(scheme);
            Properties[scheme] = properties;
            order.Add($"signout:{scheme}");
            if (scheme == OpenIdConnectDefaults.AuthenticationScheme)
            {
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = EndSessionUrl;
            }

            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string scheme) => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string scheme, AuthenticationProperties properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string scheme, AuthenticationProperties properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string scheme, ClaimsPrincipal principal, AuthenticationProperties properties) => Task.CompletedTask;
    }

    private async Task<(DefaultHttpContext Context, RecordingAuthenticationService Auth)> LogoutAsync(bool withPolicy, bool withEvaluator = true, string query = null, string endedPath = null, bool federated = true)
    {
        var builder = WebApplication.CreateBuilder();
        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig))) builder.Configuration.AddJsonStream(stream);
        builder.AddThargaAuth(o => o.FederatedSignOut = federated);
        var app = builder.Build();
        app.UseThargaAuth();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>().Single(x => x.RoutePattern.RawText == "/logout");

        var clock = new SessionClock();
        var auth = new RecordingAuthenticationService(_order);
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth);
        services.AddSingleton(_sessions.Object);
        if (withEvaluator)
        {
            services.AddSingleton(new SessionEvaluator(new SessionCache(clock), Wrap(Options()), clock, withPolicy ? new TestSessionPolicy() : null, Sessions(new InMemorySessionStore(), clock)));
        }

        services.AddSingleton(Wrap(Options(o => o.EndedPath = endedPath)));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (query != null) context.Request.QueryString = new QueryString(query);
        await endpoint.RequestDelegate(context);
        return (context, auth);
    }

    [Fact]
    public async Task Logout_WhenSessionsAreActive_EndsTheSession_AsTheCurrentCallers()
    {
        await LogoutAsync(withPolicy: true);

        _sessions.Verify(x => x.EndCurrentAsync(), Times.Once);
    }

    [Fact]
    public async Task Logout_WithNoPolicy_DoesNothingNew()
    {
        await LogoutAsync(withPolicy: false);

        _sessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Logout_WithNoSessionServicesAtAll_StillSignsOut()
    {
        var (_, auth) = await LogoutAsync(withPolicy: false, withEvaluator: false);

        Assert.Contains(CookieAuthenticationDefaults.AuthenticationScheme, auth.SignOuts);
    }

    /// <summary>The #250 guarantees, with sessions on: both halves, the provider last, and its redirect left intact.</summary>
    [Fact]
    public async Task Logout_WithSessionsOn_KeepsEverythingItAlwaysDid()
    {
        var (context, auth) = await LogoutAsync(withPolicy: true);

        Assert.Equal([CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme], auth.SignOuts);
        Assert.Equal(EndSessionUrl, context.Response.Headers.Location);
    }

    /// <summary>A session database that is down must not leave someone who asked to sign out signed in.</summary>
    [Fact]
    public async Task Logout_WhenEndingTheSessionFails_StillSignsOutBothSchemes()
    {
        _sessions.Setup(x => x.EndCurrentAsync()).ThrowsAsync(new InvalidOperationException("the session database is down"));

        var (context, auth) = await LogoutAsync(withPolicy: true);

        Assert.Equal([CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme], auth.SignOuts);
        Assert.Equal(EndSessionUrl, context.Response.Headers.Location);
    }

    /// <summary>The session is ended before the cookie goes, while the cookie still says whose session it is.</summary>
    [Fact]
    public async Task Logout_EndsTheSession_BeforeSigningOut()
    {
        _sessions.Setup(x => x.EndCurrentAsync()).Callback(() => _order.Add("end")).ReturnsAsync(true);

        await LogoutAsync(withPolicy: true);

        Assert.Equal(["end", "signout:Cookies", "signout:OpenIdConnect"], _order);
    }

    // ---- /logout?reason=signedout: the person who signed out of the "Still here?" dialog, or a tab that follows

    [Fact]
    public async Task Logout_WithTheSignedOutReason_AndSessionsActive_LandsOnTheEndedPath_WhenFederated()
    {
        var (_, auth) = await LogoutAsync(withPolicy: true, query: "?reason=signedout", endedPath: "/session-ended");

        Assert.Equal("/session-ended?reason=signedout", auth.Properties[OpenIdConnectDefaults.AuthenticationScheme].RedirectUri);
    }

    [Fact]
    public async Task Logout_WithTheSignedOutReason_AndSessionsActive_LandsOnTheEndedPath_WhenNotFederated()
    {
        var (context, _) = await LogoutAsync(withPolicy: true, query: "?reason=signedout", endedPath: "/session-ended", federated: false);

        Assert.Equal("/session-ended?reason=signedout", context.Response.Headers.Location);
    }

    [Fact]
    public async Task Logout_WithTheSignedOutReason_AndNoEndedPath_GoesToTheStartPageWithTheReason()
    {
        var (context, _) = await LogoutAsync(withPolicy: true, query: "?reason=signedout", endedPath: null, federated: false);

        Assert.Equal("/?reason=signedout", context.Response.Headers.Location);
    }

    /// <summary>Only the one literal is read. Anything else a visitor writes, including what looks like an address, is ignored, so the endpoint is no open redirect.</summary>
    [Theory]
    [InlineData("?reason=idle")]
    [InlineData("?reason=SIGNEDOUT")]
    [InlineData("?reason=signedout%20")]
    [InlineData("?reason=//evil.example")]
    [InlineData("?reason=https://evil.example")]
    [InlineData("?reason=")]
    [InlineData("?reason=signedout&reason=idle")]
    [InlineData("?returnUrl=//evil.example")]
    public async Task Logout_WithAnyOtherReason_LeavesTheRedirectAsItAlwaysWas(string query)
    {
        var (context, auth) = await LogoutAsync(withPolicy: true, query: query, endedPath: "/session-ended", federated: false);

        Assert.Equal("/", context.Response.Headers.Location);
        Assert.Equal([CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme], auth.SignOuts.Order());
    }

    [Fact]
    public async Task Logout_WithAnyOtherReason_LeavesTheFederatedRedirectAsItAlwaysWas()
    {
        var (_, auth) = await LogoutAsync(withPolicy: true, query: "?reason=idle", endedPath: "/session-ended");

        Assert.Equal("/", auth.Properties[OpenIdConnectDefaults.AuthenticationScheme].RedirectUri);
    }

    [Fact]
    public async Task Logout_WithTheSignedOutReason_AndNoSessions_DoesNothingNew()
    {
        var (context, _) = await LogoutAsync(withPolicy: false, query: "?reason=signedout", endedPath: "/session-ended", federated: false);

        Assert.Equal("/", context.Response.Headers.Location);
    }

    /// <summary>The reason changes where the browser lands and nothing else: both halves are still signed out, the provider last.</summary>
    [Fact]
    public async Task Logout_WithTheSignedOutReason_StillSignsOutBothSchemes_TheProviderLast()
    {
        var (context, auth) = await LogoutAsync(withPolicy: true, query: "?reason=signedout", endedPath: "/session-ended");

        Assert.Equal([CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme], auth.SignOuts);
        Assert.Equal(EndSessionUrl, context.Response.Headers.Location);
    }
}
