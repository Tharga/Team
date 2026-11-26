using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
namespace Tharga.Team.Blazor.Features.Authentication;

/// <summary>
/// Extension methods for registering Tharga authentication services.
/// </summary>
public static class ThargaAuthRegistration
{
    private const string AzureAdSectionName = "AzureAd";
    internal const string HomePath = "/";

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

                    await StartSessionAsync(context);
                };

                // A request refused because its session ended is sent to where it is told why, rather than straight to the provider (Tharga/Team#471).
                var innerRedirect = oidc.Events.OnRedirectToIdentityProvider;
                oidc.Events.OnRedirectToIdentityProvider = async context =>
                {
                    if (SessionCookieEvents.EndedLocation(context.HttpContext) is { } location)
                    {
                        context.Response.Redirect(location);
                        context.HandleResponse();
                        return;
                    }

                    if (innerRedirect != null) await innerRedirect(context);
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
            var landing = LogoutLanding(context);

            // First, while the cookie still says whose session it is.
            await EndSessionAsync(context);

            if (!options.FederatedSignOut)
            {
                await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Redirect(landing);
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
                new AuthenticationProperties { RedirectUri = landing });
        });
    }

    /// <summary>
    /// Where the browser lands after signing out: the start page, or, for <c>?reason=signedout</c> while sessions are on, <see cref="Framework.SessionOptions.EndedPath"/>
    /// with that reason (Tharga/Team#471), so a person who signed out of the "Still here?" dialog, or a tab that follows, is told so.
    /// </summary>
    /// <remarks>
    /// <b>Only that one literal is read</b>, once and exactly; any other value, a repeated parameter or anything that looks like an address leaves the landing
    /// at the start page. The ended path itself is the host's, and only used when it is a path on this site, so this is no open redirect.
    /// </remarks>
    internal static string LogoutLanding(HttpContext context)
    {
        var reason = context.Request.Query[SessionCookieEvents.ReasonParameterName];
        if (reason.Count != 1 || !string.Equals(reason[0], SessionCookieEvents.Code(SessionEndReason.SignedOut), StringComparison.Ordinal)) return HomePath;

        var services = context.RequestServices;
        if (services?.GetService<SessionEvaluator>()?.Active != true) return HomePath;

        var endedPath = services.GetService<IOptions<ThargaBlazorOptions>>()?.Value.Sessions?.EndedPath;
        return SessionCookieEvents.EndedLocation(endedPath, HomePath, SessionEndReason.SignedOut);
    }

    /// <summary>Where sign-in returns to: <paramref name="returnUrl"/> when it is a path on this site, otherwise the start page.</summary>
    /// <remarks>
    /// <b>Only a local path, so the login endpoint is no open redirect.</b> It starts with a single <c>/</c>; a second
    /// <c>/</c> or a <c>\</c> after it would name another host to a browser, and so would a scheme. Control characters
    /// are refused anywhere, because browsers drop tabs and line breaks and would read <c>/&#9;/host</c> as <c>//host</c>.
    /// </remarks>
    internal static string ReturnPath(string returnUrl) => IsLocalPath(returnUrl) ? returnUrl : HomePath;

    /// <summary>
    /// Starts the server-side session of an interactive sign-in, when sessions are on (Tharga/Team#471): any session claim the identity provider sent is
    /// stripped, and the library stamps its own, so the claim's provenance is the library and it travels only in the data-protected cookie.
    /// </summary>
    /// <remarks>
    /// <b>A start that fails never costs the sign-in</b>, for the same reason the audit above cannot: the principal is left without a session claim, and
    /// the next request adopts it. With no policy registered nothing here runs and the principal is untouched. The library's own service starts the
    /// session as <see cref="SessionOrigin.SignIn"/>; a host's replacement is asked through the interface.
    /// </remarks>
    private static async Task StartSessionAsync(TokenValidatedContext context)
    {
        if (context.Result != null || context.Principal == null) return;

        var services = context.HttpContext.RequestServices;
        if (services.GetService<SessionEvaluator>()?.Active != true) return;

        context.Principal = UserSessionService.WithoutSessionClaim(context.Principal);

        try
        {
            var sessions = services.GetService<IUserSessionService>();
            if (sessions == null) return;

            context.Principal = sessions is UserSessionService own
                ? await own.StartAsync(context.Principal, SessionOrigin.SignIn, context.HttpContext.RequestAborted)
                : await sessions.StartAsync(context.Principal);
        }
        catch (Exception ex)
        {
            services.GetService<ILoggerFactory>()?
                .CreateLogger(typeof(ThargaAuthRegistration))
                .LogWarning(ex, "Failed to start a server-side session at sign-in; the next request adopts the cookie.");
        }
    }

    /// <summary>
    /// Ends the caller's server-side session as signed out, when sessions are on (Tharga/Team#471); audited by the session service when this call ended it.
    /// A session database that is down must not leave someone who asked to sign out signed in, so a failure is logged and the sign-out goes on.
    /// </summary>
    private static async Task EndSessionAsync(HttpContext context)
    {
        var services = context.RequestServices;

        try
        {
            if (services?.GetService<SessionEvaluator>()?.Active != true) return;

            var sessions = services.GetService<IUserSessionService>();
            if (sessions != null) await sessions.EndCurrentAsync();
        }
        catch (Exception ex)
        {
            services?.GetService<ILoggerFactory>()?
                .CreateLogger(typeof(ThargaAuthRegistration))
                .LogWarning(ex, "Failed to end the server-side session at sign-out; the cookie is signed out regardless.");
        }
    }

    internal static bool IsLocalPath(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/') return false;
        if (value.Length > 1 && (value[1] == '/' || value[1] == '\\')) return false;

        return !value.Any(char.IsControl);
    }
}
