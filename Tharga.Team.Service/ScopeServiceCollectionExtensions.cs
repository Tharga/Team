using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Extension methods for registering scopes and scope-protected services.
/// </summary>
public static class ScopeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scope registry and configures scopes.
    /// </summary>
    public static IServiceCollection AddThargaScopes(this IServiceCollection services, Action<ScopeRegistry> configure)
    {
        var provider = services.BuildServiceProvider();
        var registry = provider.GetService<IScopeRegistry>() as ScopeRegistry;
        if (registry == null)
        {
            registry = new ScopeRegistry();
            services.AddSingleton<IScopeRegistry>(registry);
        }

        configure(registry);

        // Order-independent linkage: if the tenant-role registry is already registered, link it now so role
        // scopes resolve. (AddThargaTenantRoles performs the same link when it runs after scopes; this handles
        // the reverse order, where roles were registered first.)
        if (provider.GetService<ITenantRoleRegistry>() is { } roleRegistry)
            registry.SetRoleRegistry(roleRegistry);

        return services;
    }

    /// <summary>
    /// Registers the system-scope registry (global scopes for system API keys / privileged roles) and configures it.
    /// </summary>
    public static IServiceCollection AddThargaSystemScopes(this IServiceCollection services, Action<SystemScopeRegistry> configure)
    {
        var registry = services.BuildServiceProvider().GetService<ISystemScopeRegistry>() as SystemScopeRegistry;
        if (registry == null)
        {
            registry = new SystemScopeRegistry();
            services.AddSingleton<ISystemScopeRegistry>(registry);
        }

        configure(registry);
        return services;
    }

    /// <summary>
    /// Registers the system-role registry (maps app/global roles to system scopes) and configures it.
    /// </summary>
    public static IServiceCollection AddThargaSystemRoles(this IServiceCollection services, Action<SystemRoleRegistry> configure)
    {
        var registry = services.BuildServiceProvider().GetService<ISystemRoleRegistry>() as SystemRoleRegistry;
        if (registry == null)
        {
            registry = new SystemRoleRegistry();
            services.AddSingleton<ISystemRoleRegistry>(registry);
        }

        configure(registry);
        return services;
    }

    /// <summary>
    /// Registers a service whose every operation acts on one team. The scope declared by
    /// <see cref="RequireScopeAttribute"/> is checked against the team named by the call's first argument,
    /// so holding the scope for one team does not authorize acting on another.
    /// </summary>
    public static IServiceCollection AddTeamService<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
        => AddScopedWithScopes<TService, TImplementation>(services, ServiceScopeKind.Team);

    /// <summary>
    /// Registers a service whose operations act across the whole system rather than on a team. The scope
    /// is checked as a system scope and no team need be selected.
    /// </summary>
    public static IServiceCollection AddSystemService<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
        => AddScopedWithScopes<TService, TImplementation>(services, ServiceScopeKind.System);

    /// <summary>
    /// Registers a scoped service wrapped in a <see cref="ScopeProxy{T}"/> that enforces
    /// <see cref="RequireScopeAttribute"/> on every method call. Prefer the intent-revealing
    /// <see cref="AddTeamService{TService,TImplementation}"/> and
    /// <see cref="AddSystemService{TService,TImplementation}"/>.
    /// </summary>
    public static IServiceCollection AddScopedWithScopes<TService, TImplementation>(this IServiceCollection services, ServiceScopeKind scopeKind)
        where TService : class
        where TImplementation : class, TService
    {
        ServiceScopeValidation.Validate(typeof(TService), scopeKind);

        services.AddHttpContextAccessor();
        services.TryAddScoped<ITeamPrincipalAccessor, HttpContextTeamPrincipalAccessor>();
        services.AddScoped<TImplementation>();
        services.AddScoped<TService>(sp =>
        {
            var target = sp.GetRequiredService<TImplementation>();
            var principalAccessor = sp.GetRequiredService<ITeamPrincipalAccessor>();
            var auditLogger = sp.GetService<CompositeAuditLogger>();
            var defaultAuditMode = sp.GetService<IOptions<AuditOptions>>()?.Value.DefaultAuditMode ?? AuditMode.Access;
            return ScopeProxy<TService>.Create(target, principalAccessor, scopeKind, auditLogger, defaultAuditMode);
        });
        return services;
    }
}
