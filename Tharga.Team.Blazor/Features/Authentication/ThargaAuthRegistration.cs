using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;

using Microsoft.Extensions.Logging;
using Tharga.Team.Service.Audit;
namespace Tharga.Team.Blazor.Features.Authentication;

/// <summary>
/// Extension methods for registering Tharga authentication services.
/// </summary>
public static class ThargaAuthRegistration
{
    private const string AzureAdSectionName = "AzureAd";
    private const string HomePath = "/";

    /// <summary>
    /// The key the OpenID Connect handler reads a prompt from, and the value that asks for an account
    /// picker. Both are protocol constants rather than names of ours.
    /// </summary>
    private const string PromptParameterName = "prompt";
    private const string SelectAccountPrompt = "select_account";

    /// <summary>The query parameter the login endpoint reads where to return to after sign-in.</summary>
    internal const string ReturnUrlParameterName = "returnUrl";

    private const string MissingConfigMessage =
        $"Missing '{AzureAdSectionName}' configuration section. " +
        "Add an AzureAd section to appsettings.json with Authority, ClientId, TenantId, and CallbackPath. " +
        "To disable this check, set ThargaAuthOptions.ValidateConfiguration = false.";

    /// <summary>
    /// Registers Azure AD (CIAM) authentication services with Cookie default scheme and OIDC challenge scheme.
    /// </summary>
    public static void AddThargaAuth(this WebApplicationBuilder builder, Action<ThargaAuthOptions> configure = null)
    {
        var options = new ThargaAuthOptions();
        configure?.Invoke(options);

        var azureAdSection = builder.Configuration.GetSection(AzureAdSectionName);

        if (options.ValidateConfiguration && !azureAdSection.Exists())
        {
            throw new InvalidOperationException(MissingConfigMessage);
        }

        builder.Services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddMicrosoftIdentityWebApp(azureAdSection);

        // Audit the interactive sign-in. OnTokenValidated fires once per sign-in, unlike the claims
        // transformation which runs on every authenticating request -- so this records arrival, not traffic.
        // Tharga/Team#142 names "user logs on" as its first event and nothing raised it before.
        builder.Services.Configure<OpenIdConnectOptions>(
            OpenIdConnectDefaults.AuthenticationScheme,
            oidc =>
            {
                oidc.Events ??= new OpenIdConnectEvents();

                var inner = oidc.Events.OnTokenValidated;
                oidc.Events.OnTokenValidated = async context =>
                {
                    // A failure here must never cost anyone their sign-in: an audit entry observes something
                    // that already happened, so it cannot be allowed to undo it.
                    try
                    {
                        var services = context.HttpContext.RequestServices;
                        services.GetService<CompositeAuditLogger>()
                            ?.Log(AuthAuditEntries.SignIn(context.Principal, services.GetService<UserIdentityResolver>()));
                    }
                    catch (Exception ex)
                    {
                        context.HttpContext.RequestServices
                            .GetService<ILoggerFactory>()?
                            .CreateLogger(typeof(ThargaAuthRegistration))
                            .LogWarning(ex, "Failed to audit an interactive sign-in.");
                    }

                    if (inner != null) await inner(context);
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddSingleton(options);
    }

    /// <summary>
    /// Maps login and logout endpoints for Azure AD (CIAM) authentication.
    /// </summary>
    public static void UseThargaAuth(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<ThargaAuthOptions>();

        app.MapGet(options.LoginPath, async (HttpContext context) =>
        {
            var properties = new AuthenticationProperties { RedirectUri = ReturnPath(context.Request.Query[ReturnUrlParameterName].ToString()) };

            // The handler reads the prompt out of Items and removes it before building the request; there is
            // no property on the challenge itself to set.
            if (options.PromptForAccount) properties.Items[PromptParameterName] = SelectAccountPrompt;

            await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
        });

        app.MapGet(options.LogoutPath, async (HttpContext context) =>
        {
            if (!options.FederatedSignOut)
            {
                await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Redirect(HomePath);
                return;
            }

            // Order is the fix, not a tidy-up. Signing out of the OpenID Connect scheme does not end anything
            // in process: it writes the provider's end_session_endpoint into the response's Location header
            // and relies on the browser going there. So it has to run last and own the response -- writing a
            // redirect after it replaces that header, the browser never reaches the provider, and the
            // provider's session outlives a sign-out the application has already reported as done.
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(
                OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = HomePath });
        });
    }

    /// <summary>Where sign-in returns to: <paramref name="returnUrl"/> when it is a path on this site, otherwise the start page.</summary>
    /// <remarks>
    /// <b>Only a local path, so the login endpoint is no open redirect.</b> It starts with a single <c>/</c>; a second
    /// <c>/</c> or a <c>\</c> after it would name another host to a browser, and so would a scheme. Control characters
    /// are refused anywhere, because browsers drop tabs and line breaks and would read <c>/&#9;/host</c> as <c>//host</c>.
    /// </remarks>
    internal static string ReturnPath(string returnUrl) => IsLocalPath(returnUrl) ? returnUrl : HomePath;

    private static bool IsLocalPath(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/') return false;
        if (value.Length > 1 && (value[1] == '/' || value[1] == '\\')) return false;

        return !value.Any(char.IsControl);
    }
}
