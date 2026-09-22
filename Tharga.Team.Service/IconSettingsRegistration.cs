using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service;

public static class IconSettingsRegistration
{
    /// <summary>
    /// Registers <see cref="IIconSettingsService"/>: the domain service, the audit decorator when audit is
    /// configured, and the <c>ScopeProxy</c> that enforces the system <c>users:manage</c> grant.
    /// </summary>
    /// <remarks>
    /// <b>The order of the three matters.</b> The proxy is outermost, so an unauthorized call is refused
    /// before the decorator writes an entry claiming the change happened. The decorator sits inside it and
    /// records what was stored. Built by hand rather than through <c>AddSystemService</c> because that
    /// composes the proxy directly onto the implementation type, leaving nowhere for a decorator to sit.
    /// </remarks>
    public static IServiceCollection AddThargaIconSettings(this IServiceCollection services)
    {
        ServiceScopeValidation.Validate(typeof(IIconSettingsService), ServiceScopeKind.System);

        services.AddHttpContextAccessor();
        services.TryAddScoped<ITeamPrincipalAccessor, HttpContextTeamPrincipalAccessor>();
        services.TryAddScoped<IconSettingsService>();

        services.TryAddScoped<IIconSettingsService>(sp =>
        {
            IIconSettingsService inner = sp.GetRequiredService<IconSettingsService>();

            var auditLogger = sp.GetService<CompositeAuditLogger>();
            if (auditLogger != null)
                inner = new AuditingIconSettingsServiceDecorator(inner, auditLogger, sp.GetRequiredService<IHttpContextAccessor>());

            var principalAccessor = sp.GetRequiredService<ITeamPrincipalAccessor>();
            var defaultAuditMode = sp.GetService<IOptions<AuditOptions>>()?.Value.DefaultAuditMode ?? AuditMode.Access;

            return ScopeProxy<IIconSettingsService>.Create(inner, principalAccessor, ServiceScopeKind.System, auditLogger, defaultAuditMode);
        });

        return services;
    }
}
