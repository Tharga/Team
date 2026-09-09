using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service;

/// <summary>
/// Extension methods for registering services with automatic access level enforcement.
/// </summary>
public static class AccessLevelServiceCollectionExtensions
{
    /// <summary>
    /// Registers a scoped service wrapped in an <see cref="AccessLevelProxy{T}"/>
    /// that enforces <see cref="RequireAccessLevelAttribute"/> on every method call.
    /// Logs audit entries when IAuditLogger is available.
    /// </summary>
    public static IServiceCollection AddScopedWithAccessLevel<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<ITeamPrincipalAccessor, HttpContextTeamPrincipalAccessor>();
        services.AddScoped<TImplementation>();
        services.AddScoped<TService>(sp =>
        {
            var target = sp.GetRequiredService<TImplementation>();
            var principalAccessor = sp.GetRequiredService<ITeamPrincipalAccessor>();
            var auditLogger = sp.GetService<IAuditLogger>();
            var defaultAuditMode = sp.GetService<IOptions<AuditOptions>>()?.Value.DefaultAuditMode ?? AuditMode.Access;
            return AccessLevelProxy<TService>.Create(target, principalAccessor, auditLogger, defaultAuditMode);
        });
        return services;
    }
}
