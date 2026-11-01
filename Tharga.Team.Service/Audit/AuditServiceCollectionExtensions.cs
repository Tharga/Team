using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Extension methods for registering audit logging.
/// </summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers audit logging with the specified options.
    /// </summary>
    public static IServiceCollection AddThargaAuditLogging(this IServiceCollection services, Action<AuditOptions> configure = null)
    {
        var options = new AuditOptions();
        configure?.Invoke(options);
        services.Configure<AuditOptions>(o =>
        {
            o.StorageMode = options.StorageMode;
            o.CallerFilter = options.CallerFilter;
            o.EventFilter = options.EventFilter;
            o.ExcludedActions = options.ExcludedActions;
            o.ExcludedEndpoints = options.ExcludedEndpoints;
            o.RetentionDays = options.RetentionDays;
            o.BatchSize = options.BatchSize;
            o.FlushIntervalSeconds = options.FlushIntervalSeconds;
        });

        // Lets work with no HTTP caller declare who it is. Registered unconditionally and regardless of
        // storage mode: a host that audits at all may have a background job, and the accessor costs
        // nothing until something pushes a scope.
        services.TryAddSingleton<IAuditContextAccessor, AuditContextAccessor>();

        // The consumer's route into that actor. IAuditLogger takes a pre-built entry, so without this a
        // host writing its own entries would construct them by hand and the declared actor would be
        // ignored — the scope would be machinery wired to nothing.
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IAuditEntryFactory, AuditEntryFactory>();

        if (options.StorageMode.HasFlag(AuditStorageMode.Logger))
        {
            services.AddSingleton<LoggerAuditLogger>();
            services.AddSingleton<IAuditLogger>(sp => sp.GetRequiredService<LoggerAuditLogger>());
        }

        if (options.StorageMode.HasFlag(AuditStorageMode.MongoDB))
        {
            services.AddTransient<IAuditRepositoryCollection, AuditRepositoryCollection>();
            services.AddSingleton<MongoDbAuditLogger>();
            services.AddSingleton<IAuditLogger>(sp => sp.GetRequiredService<MongoDbAuditLogger>());
            services.AddHostedService(sp => sp.GetRequiredService<MongoDbAuditLogger>());
            services.AddHostedService<AuditRetentionIndexService>();
        }

        // Register as concrete type only — NOT as IAuditLogger to avoid circular dependency.
        // CompositeAuditLogger takes IEnumerable<IAuditLogger> in its constructor,
        // so registering it as IAuditLogger would create a circular resolution deadlock.
        services.AddSingleton<CompositeAuditLogger>();

        // Registered twice, deliberately: the same implementation behind a team-bound interface and a
        // system one, so ScopeProxy applies a different rule to each. Reading one team requires the grant
        // on that team; reading across all of them requires the system grant, and no call on the
        // team-bound interface can reach past the team it names.
        services.AddScoped<TeamContextResolver>();
        services.AddScoped<AuditReadService>();
        services.AddTeamService<IAuditReadService, AuditReadService>();
        services.AddSystemService<IAuditOversightService, AuditReadService>();

        return services;
    }

    /// <summary>
    /// Registers an <see cref="IAuditEnricher"/> that adds host-defined metadata to every audit entry
    /// the toolkit writes. Multiple enrichers may be registered; they run in registration order.
    /// </summary>
    /// <remarks>
    /// The enricher is resolved as a singleton (<see cref="CompositeAuditLogger"/> is a singleton), so
    /// read per-request state through <c>IHttpContextAccessor</c> rather than a scoped dependency.
    /// </remarks>
    public static IServiceCollection AddThargaAuditEnricher<TEnricher>(this IServiceCollection services)
        where TEnricher : class, IAuditEnricher
    {
        services.AddSingleton<IAuditEnricher, TEnricher>();
        return services;
    }
}
