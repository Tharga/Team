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
                        context.HttpContext.RequestServices
                            .GetService<CompositeAuditLogger>()
                            ?.Log(AuthAuditEntries.SignIn(context.Principal));
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
            await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = "/" });
        });

        app.MapGet(options.LogoutPath, async (HttpContext context) =>
        {
            await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/");
        });
    }
}
