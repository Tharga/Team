using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service;

/// <summary>
/// Extension methods for registering API key authentication.
/// </summary>
public static class ApiKeyRegistration
{
    /// <summary>
    /// Adds API key authentication with the default <see cref="ApiKeyAdministrationService"/>.
    /// </summary>
    public static AuthenticationBuilder AddThargaApiKeyAuthentication(this AuthenticationBuilder builder, Action<ApiKeyOptions> configureOptions = null)
    {
        return builder.AddThargaApiKeyAuthentication<ApiKeyAdministrationService>(configureOptions);
    }

    /// <summary>
    /// Adds API key authentication with a custom <see cref="IApiKeyAdministrationService"/> implementation.
    /// </summary>
    public static AuthenticationBuilder AddThargaApiKeyAuthentication<TService>(this AuthenticationBuilder builder, Action<ApiKeyOptions> configureOptions = null)
        where TService : class, IApiKeyAdministrationService
    {
        if (configureOptions != null)
            builder.Services.Configure(configureOptions);
        else
            builder.Services.Configure<ApiKeyOptions>(_ => { });

        builder.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
            ApiKeyConstants.SchemeName, "API Key", null);

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(ApiKeyConstants.PolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(ApiKeyConstants.SchemeName);
                policy.RequireAuthenticatedUser();
                // Team policy rejects system keys — a team key has no IsSystemKey claim set to "true"
                policy.RequireAssertion(ctx =>
                    !ctx.User.HasClaim(TeamClaimTypes.IsSystemKey, "true"));
            });

            options.AddPolicy(ApiKeyConstants.SystemPolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(ApiKeyConstants.SchemeName);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(TeamClaimTypes.IsSystemKey, "true");
            });

            // Deliberately asserts nothing about IsSystemKey. The two policies above are disjoint, so
            // requiring both admits nothing — this is the only built-in way to reach an endpoint with
            // either kind of key.
            options.AddPolicy(ApiKeyConstants.AnyKeyPolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(ApiKeyConstants.SchemeName);
                policy.RequireAuthenticatedUser();
            });
        });

        builder.Services.AddAuditedApiKeyAdministrationService(typeof(TService));

        return builder;
    }

    /// <summary>
    /// Registers <paramref name="implementationType"/> as the <see cref="IApiKeyAdministrationService"/>,
    /// applying the <see cref="AuditingApiKeyServiceDecorator"/> at resolve time when audit logging is
    /// configured (a <see cref="CompositeAuditLogger"/> is present). This is the single owner of the
    /// <see cref="IApiKeyAdministrationService"/> registration: it drops any prior registration first, so
    /// call order between Tharga entry points can no longer discard the audit decorator.
    /// </summary>
    internal static void AddAuditedApiKeyAdministrationService(this IServiceCollection services, Type implementationType)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IApiKeyAdministrationService)).ToList())
            services.Remove(descriptor);

        services.AddScoped(implementationType);
        services.AddScoped(typeof(IApiKeyAdministrationService), sp =>
        {
            var inner = (IApiKeyAdministrationService)sp.GetRequiredService(implementationType);
            var auditLogger = sp.GetService<CompositeAuditLogger>(); // deferred — audit may be added after this registration
            if (auditLogger == null) return inner;

            var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            return new AuditingApiKeyServiceDecorator(inner, auditLogger, httpContextAccessor);
        });
    }
}
