using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Single entry point for registering all Tharga Team services.
/// </summary>
public static class ThargaTeamRegistration
{
    /// <summary>Named <see cref="System.Net.Http.HttpClient"/> used to download icons supplied by URL.</summary>
    internal const string IconHttpClientName = "tharga-icon-download";

    /// <summary>
    /// Registers all Tharga Team services with sensible defaults.
    /// Call <see cref="UseThargaTeam"/> on the built WebApplication to configure middleware.
    /// </summary>
    public static void AddThargaTeam(this WebApplicationBuilder builder, Action<ThargaTeamOptions> configure = null)
    {
        var options = new ThargaTeamOptions();
        configure?.Invoke(options);
        AddThargaTeamCore(builder, options);
    }

    /// <summary>
    /// Registration against an already-configured options instance, so the obsolete
    /// <c>AddThargaPlatform</c> entry point runs exactly this logic rather than a copy of it.
    /// </summary>
    internal static void AddThargaTeamCore(WebApplicationBuilder builder, ThargaTeamOptions options)
    {
        // Consent policy, registered as the one instance every surface reads -- the Blazor claims builder
        // and an MCP call naming a team both resolve IOptions<ConsentOptions> and therefore cannot
        // disagree about which roles a team may consent to, or at what level.
        builder.Services.AddSingleton(Options.Create(options.Blazor.Consent));

        builder.Services.AddSingleton(new UserIdentityResolver(options.UserIdentityClaimTypes));

        // Auth (Azure AD + OIDC)
        builder.AddThargaAuth(o =>
        {
            o.LoginPath = options.Auth.LoginPath;
            o.LogoutPath = options.Auth.LogoutPath;
            o.ValidateConfiguration = options.Auth.ValidateConfiguration;
        });

        // API key authentication scheme
        if (options.ApiKey != null)
        {
            builder.Services
                .AddAuthentication()
                .AddThargaApiKeyAuthentication(o =>
                {
                    // Keep in sync with ApiKeyOptions — forward every setting the consumer set on o.ApiKey.
                    o.AdvancedMode = options.ApiKey.AdvancedMode;
                    o.AutoKeyCount = options.ApiKey.AutoKeyCount;
                    o.AutoLockKeys = options.ApiKey.AutoLockKeys;
                    o.MaxExpiryDays = options.ApiKey.MaxExpiryDays;
                    o.LastUsedThrottle = options.ApiKey.LastUsedThrottle;
                    o.MinKeyLength = options.ApiKey.MinKeyLength;
                    o.MaxKeyLength = options.ApiKey.MaxKeyLength;
                });

            builder.Services.AddThargaApiKeys();
        }

        // Blazor UI layer — pass the pre-configured options object directly
        builder.Services.AddThargaTeamBlazor(o =>
        {
            // Every public option, by reflection. This used to be a hand-written list of nine
            // assignments, which meant an option added later was accepted from the caller and silently
            // discarded — configured, no error, and simply never in effect. See the forwarder.
            ThargaBlazorOptionsForwarder.Copy(options.Blazor, o);

            o._teamService = options.Blazor._teamService;
            o._userService = options.Blazor._userService;
            o._memberType = options.Blazor._memberType;
            o._apiKeyService = options.Blazor._apiKeyService;
            o._claimsEnricher = options.Blazor._claimsEnricher;

            // Icon configuration lives on the facade's own options for backwards compatibility, so forward
            // it into the layer that now registers the chain.
            o.Icon = options.Icon;
            o.IconSettings = options.IconSettings;
            o._iconStoreType = options._iconStoreType;
            o._iconSourceTypes.AddRange(options._iconSourceTypes);

            // Email, the same way and for the same reason. Assigned only when the facade's own option is set,
            // so a host that configured o.Blazor.Email directly is not overwritten with null by the forwarder
            // above having already copied it.
            if (options.Email != null) o.Email = options.Email;
            if (options._emailSenderType != null) o._emailSenderType = options._emailSenderType;
        }, builder.Configuration);

        // API key lifecycle handlers (opt-in) — wrap IApiKeyAdministrationService once and register the
        // handlers. Done after the API key + audit registration above so the decorator composes on top.
        foreach (var handlerType in options._apiKeyLifecycleHandlers)
        {
            builder.Services.AddThargaApiKeyLifecycleHandler(handlerType);
        }

        // Controllers + Swagger
        if (options.Controllers != null)
        {
            builder.Services.AddThargaControllers(o =>
            {
                o.SwaggerTitle = options.Controllers.SwaggerTitle;
                o.SwaggerRoutePrefix = options.Controllers.SwaggerRoutePrefix;
            });
        }

        // Scopes (opt-in)
        if (options.ConfigureScopes != null)
        {
            builder.Services.AddThargaScopes(options.ConfigureScopes);
        }

        // Tenant roles (opt-in, requires scopes)
        if (options.ConfigureTenantRoles != null)
        {
            builder.Services.AddThargaTenantRoles(options.ConfigureTenantRoles);
        }

        // Dynamic (runtime-defined) tenant roles (opt-in) — team-aware scope resolution incl. custom roles.
        if (options.EnableDynamicRoles)
        {
            builder.Services.AddThargaDynamicTenantRoles(o =>
            {
                if (!string.IsNullOrWhiteSpace(options.DynamicRoleManageScope))
                    o.ManageScope = options.DynamicRoleManageScope;
            });
        }

        // System scopes (opt-in) — global capabilities for system keys / privileged roles
        if (options.ConfigureSystemScopes != null)
        {
            builder.Services.AddThargaSystemScopes(options.ConfigureSystemScopes);
        }

        // System roles (opt-in) — map app/global roles to system scopes for privileged users.
        // Consent.GrantTeamsRead composes on top of any consumer mapping rather than replacing it.
        var grantTeamsRead = options.Blazor.Consent is { GrantTeamsRead: true, Roles.Length: > 0 };
        if (options.ConfigureSystemRoles != null || grantTeamsRead)
        {
            builder.Services.AddThargaSystemRoles(roles =>
            {
                options.ConfigureSystemRoles?.Invoke(roles);

                if (!grantTeamsRead) return;

                foreach (var role in options.Blazor.Consent.Roles)
                {
                    roles.Grant(role, SystemTeamScopes.Read);
                }
            });
        }

        // Audit logging (opt-in)
        if (options.Audit != null)
        {
            builder.Services.AddThargaAuditLogging(o =>
            {
                o.StorageMode = options.Audit.StorageMode;
                o.CallerFilter = options.Audit.CallerFilter;
                o.EventFilter = options.Audit.EventFilter;
                o.ExcludedActions = options.Audit.ExcludedActions;
                o.ExcludedEndpoints = options.Audit.ExcludedEndpoints;
                o.RetentionDays = options.Audit.RetentionDays;
                o.BatchSize = options.Audit.BatchSize;
                o.FlushIntervalSeconds = options.Audit.FlushIntervalSeconds;
            });
        }

        // Custom user directory provider (opt-in). Entra consumers call AddThargaEntraUserDirectory instead.
        if (options._userDirectoryServiceType != null)
        {
            builder.Services.AddScoped(typeof(IUserDirectoryService), options._userDirectoryServiceType);
        }

        // Icons are registered by AddThargaTeamBlazor, which this method already calls — see the options
        // forwarding above. They used to be registered here, which left the granular path unable to render
        // LoginDisplay at all (Tharga/Team#157).

        // The email sender is registered by AddThargaTeamBlazor, which this method already calls -- see the
        // options forwarding above. It used to be registered here, which left the granular path unable to send
        // invitations at all (Tharga/Team#176), and silently: the dialogs resolve the sender with GetService
        // and degrade to manual link copying, so nothing reported the gap.
    }

    /// <summary>
    /// Configures Tharga Team middleware (auth endpoints, controllers, Swagger).
    /// </summary>
    public static void UseThargaTeam(this WebApplication app)
    {
        app.UseThargaAuth();

        var controllerOptions = app.Services.GetService<ThargaControllerOptions>();
        if (controllerOptions != null)
        {
            app.UseThargaControllers();
        }

        app.UseThargaTeamBlazor();
    }

    /// <summary>
    /// Serves stored icons at <see cref="IconRoute.Base"/>/{reference} for authenticated callers. The
    /// reference changes whenever the icon changes, so a served URL is immutable and cached aggressively.
    /// </summary>
}
