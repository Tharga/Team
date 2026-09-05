using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Features.Authentication;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What the <c>/login</c> and <c>/logout</c> endpoints actually <b>do</b>, as opposed to whether they are
/// mapped.
/// </summary>
/// <remarks>
/// <b>This file exists because its absence let Tharga/Team#250 ship.</b> `ThargaAuthRegistrationTests`
/// asserts both routes are registered, and every one of those tests passed throughout: the defect was
/// entirely inside a handler body that nothing invoked. Sign-out wrote the provider's
/// <c>end_session_endpoint</c> into the response's <c>Location</c> header and then overwrote it with a local
/// redirect, so the identity-provider session survived a logout the application reported as successful.
/// <para>
/// The seam is the endpoint's own <see cref="RouteEndpoint.RequestDelegate"/>, invoked against a
/// <see cref="DefaultHttpContext"/> whose <see cref="IAuthenticationService"/> is a recorder. That is enough
/// to observe both halves of the bug — which schemes were signed out, and what survived in the response.
/// </para>
/// </remarks>
public class ThargaAuthEndpointBehaviourTests
{
    /// <summary>Stands in for the URL the real OIDC handler writes when it signs out.</summary>
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

    /// <summary>
    /// Records what each endpoint asks of the authentication stack, and imitates the one behaviour of the
    /// real OIDC handler that this bug turns on: signing out writes a redirect to the provider rather than
    /// signing anything out in-process.
    /// </summary>
    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public List<(string Scheme, AuthenticationProperties Properties)> SignOuts { get; } = [];
        public List<(string Scheme, AuthenticationProperties Properties)> Challenges { get; } = [];

        public Task SignOutAsync(HttpContext context, string scheme, AuthenticationProperties properties)
        {
            SignOuts.Add((scheme, properties));

            if (scheme == OpenIdConnectDefaults.AuthenticationScheme)
            {
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = EndSessionUrl;
            }

            return Task.CompletedTask;
        }

        public Task ChallengeAsync(HttpContext context, string scheme, AuthenticationProperties properties)
        {
            Challenges.Add((scheme, properties));
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

        public Task ForbidAsync(HttpContext context, string scheme, AuthenticationProperties properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string scheme, ClaimsPrincipal principal, AuthenticationProperties properties)
            => Task.CompletedTask;
    }

    private static async Task<(DefaultHttpContext Context, RecordingAuthenticationService Auth)> InvokeAsync(
        string path,
        Action<ThargaAuthOptions> configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig)))
        {
            builder.Configuration.AddJsonStream(stream);
        }

        builder.AddThargaAuth(configure);
        var app = builder.Build();
        app.UseThargaAuth();

        var endpoint = ((IEndpointRouteBuilder)app)
            .DataSources
            .SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(x => x.RoutePattern.RawText == path);

        var auth = new RecordingAuthenticationService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        await endpoint.RequestDelegate(context);

        return (context, auth);
    }

    /// <summary>
    /// <b>The defect.</b> The provider sign-out is a redirect to the <c>end_session_endpoint</c>; anything
    /// that replaces it means the browser never makes that trip and the provider session stays alive.
    /// </summary>
    [Fact]
    public async Task Logout_LeavesTheProviderSignOutRedirectIntact()
    {
        var (context, _) = await InvokeAsync("/logout");

        Assert.Equal(EndSessionUrl, context.Response.Headers.Location);
    }

    /// <summary>Both halves are needed: the local cookie and the provider session.</summary>
    [Fact]
    public async Task Logout_SignsOutBothSchemes()
    {
        var (_, auth) = await InvokeAsync("/logout");

        Assert.Contains(auth.SignOuts, x => x.Scheme == CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Contains(auth.SignOuts, x => x.Scheme == OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// The provider sign-out has to carry where to come back to, because nothing after it may write a
    /// redirect of its own.
    /// </summary>
    [Fact]
    public async Task Logout_AsksTheProviderToReturnHome()
    {
        var (_, auth) = await InvokeAsync("/logout");

        var oidc = auth.SignOuts.Single(x => x.Scheme == OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal("/", oidc.Properties?.RedirectUri);
    }

    /// <summary>
    /// Ordering matters and is not incidental: the provider sign-out owns the response, so it must run last.
    /// </summary>
    [Fact]
    public async Task Logout_SignsOutTheProviderLast()
    {
        var (_, auth) = await InvokeAsync("/logout");

        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, auth.SignOuts[^1].Scheme);
    }

    [Fact]
    public async Task Login_ChallengesTheProviderAndReturnsHome()
    {
        var (_, auth) = await InvokeAsync("/login");

        var challenge = Assert.Single(auth.Challenges);

        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, challenge.Scheme);
        Assert.Equal("/", challenge.Properties?.RedirectUri);
    }

    /// <summary>
    /// The escape hatch restores the old sequence exactly, local redirect included — a host that opts out
    /// gets what it had, not a third behaviour.
    /// </summary>
    [Fact]
    public async Task Logout_WithFederatedSignOutDisabled_KeepsTheLocalRedirect()
    {
        var (context, auth) = await InvokeAsync("/logout", o => o.FederatedSignOut = false);

        Assert.Equal("/", context.Response.Headers.Location);
        Assert.Contains(auth.SignOuts, x => x.Scheme == CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Asserted on the properties handed to the authentication stack, not on the option. A wrong key would
    /// leave the option set, every option test passing, and the browser still sent without a prompt.
    /// </summary>
    [Fact]
    public async Task Login_WithPromptForAccount_AsksForTheAccountPicker()
    {
        var (_, auth) = await InvokeAsync("/login", o => o.PromptForAccount = true);

        var challenge = Assert.Single(auth.Challenges);

        Assert.True(challenge.Properties.Items.TryGetValue("prompt", out var prompt));
        Assert.Equal("select_account", prompt);
    }

    /// <summary>Off by default, so nobody gains a click on every sign-in by upgrading.</summary>
    [Fact]
    public async Task Login_ByDefault_SendsNoPrompt()
    {
        var (_, auth) = await InvokeAsync("/login");

        var challenge = Assert.Single(auth.Challenges);

        Assert.DoesNotContain("prompt", challenge.Properties.Items.Keys);
    }

    /// <summary>The behaviour has to follow a host that moved the endpoints, not just the default paths.</summary>
    [Fact]
    public async Task CustomPaths_BehaveTheSameWay()
    {
        var (context, auth) = await InvokeAsync("/sign-out", o => o.LogoutPath = "/sign-out");

        Assert.Equal(EndSessionUrl, context.Response.Headers.Location);
        Assert.Equal(OpenIdConnectDefaults.AuthenticationScheme, auth.SignOuts[^1].Scheme);
    }
}
