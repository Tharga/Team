using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;
using Tharga.Toolkit.Password;

namespace Tharga.Team.Service;

/// <summary>
/// Extension methods for registering API controllers with OpenAPI and Swagger.
/// </summary>
public static class ControllersRegistration
{
    /// <summary>
    /// Registers MVC controllers, OpenAPI document with API key security scheme, Swagger, and endpoints API explorer.
    /// </summary>
    public static IServiceCollection AddThargaControllers(this IServiceCollection services, Action<ThargaControllerOptions> configure = null)
    {
        var options = new ThargaControllerOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddControllers();

        // The policy the toolkit's own endpoints use, built from the configured schemes so an API key is
        // accepted without the host naming a scheme — see ThargaControllerOptions.AuthenticationSchemes.
        services.AddAuthorizationBuilder()
            .AddPolicy(ApiKeyConstants.ThargaApiPolicyName, policy =>
            {
                if (options.AuthenticationSchemes.Count > 0)
                    policy.AddAuthenticationSchemes([.. options.AuthenticationSchemes]);

                policy.RequireAuthenticatedUser();
            });

#if NET10_0_OR_GREATER
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((document, _, _) =>
            {
                var schemes = document.Components?.SecuritySchemes
                              ?? new Dictionary<string, IOpenApiSecurityScheme>();

                schemes[ApiKeyConstants.OpenApiSchemeId] = new OpenApiSecurityScheme
                {
                    Name = ApiKeyConstants.HeaderName,
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Description = "API key for authentication"
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes = schemes;

                document.Security ??= [];
                var requirement = new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(ApiKeyConstants.OpenApiSchemeId, document)] = []
                };
                document.Security.Add(requirement);

                return Task.CompletedTask;
            });

            options.OpenApiConfigure?.Invoke(o);
        });
#endif

        services.AddSwaggerGen(o =>
        {
            o.AddSecurityDefinition(ApiKeyConstants.OpenApiSchemeId, new OpenApiSecurityScheme
            {
                Name = ApiKeyConstants.HeaderName,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "API key for authentication"
            });
            o.AddSecurityRequirement(document =>
            {
                var requirement = new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(ApiKeyConstants.OpenApiSchemeId, document)] = []
                };
                return requirement;
            });
        });
        services.AddEndpointsApiExplorer();

        return services;
    }

    /// <summary>
    /// Registers the default API key storage, administration service, and hashing service.
    /// Also registers the MongoDB repository types explicitly so they are available
    /// regardless of the entry assembly name prefix used by the consumer.
    /// </summary>
    public static IServiceCollection AddThargaApiKeys(this IServiceCollection services)
    {
        services.RegisterApiKeyService();
        services.AddTeamService<IApiKeyManagementService, ApiKeyManagementService>();
        services.AddSystemService<ISystemApiKeyManagementService, SystemApiKeyManagementService>();
        services.AddTransient<IApiKeyRepository, ApiKeyRepository>();
        services.AddTransient<IApiKeyRepositoryCollection, ApiKeyRepositoryCollection>();

        // Purging a team must destroy its API keys. Purge drops the host's per-team database, which does
        // not reach this shared collection -- so without this a purged tenant's credentials outlive it.
        //
        // Scoped, not singleton: every participant reads a store, and those stores are scoped. A singleton
        // capturing them fails container validation at startup rather than at purge time.
        services.TryAddScoped<TeamPurgeCascade>();
        services.AddTransient<ITeamPurgeParticipant, ApiKeyPurgeParticipant>();

        return services;
    }

    /// <summary>
    /// Maps controllers, OpenAPI endpoint, and Swagger UI, and resolves the team a request acts on.
    /// </summary>
    /// <remarks>
    /// <b>Call this after <c>UseAuthorization()</c>.</b> An API key is authenticated by the policy that
    /// names its scheme, and that happens during authorization — so the team-context middleware placed
    /// before it would see an unauthenticated caller and silently do nothing, leaving the header ignored
    /// rather than refused. Found by an end-to-end test; no unit test of the middleware could show it,
    /// because the ordering is the whole defect.
    /// </remarks>
    public static WebApplication UseThargaControllers(this WebApplication app)
    {
        var options = app.Services.GetService<ThargaControllerOptions>() ?? new ThargaControllerOptions();

        // Before the controllers, so a system key naming a team in the header carries its claims into
        // every endpoint -- including a host's own, which need no knowledge of the mechanism.
        app.UseMiddleware<TeamContextMiddleware>();

        app.MapControllers();
#if NET10_0_OR_GREATER
        app.MapOpenApi();
#endif
        app.UseSwaggerUI(o =>
        {
            o.RoutePrefix = options.SwaggerRoutePrefix;
            o.SwaggerEndpoint("/openapi/v1.json", options.SwaggerTitle);
        });

        return app;
    }
}
