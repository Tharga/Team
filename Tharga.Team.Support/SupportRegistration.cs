using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Notifications;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support;

/// <summary>
/// Registration for the support module.
/// </summary>
public static class SupportRegistration
{
    /// <summary>
    /// Registers Slack notifications: audited events are matched against
    /// <see cref="NotificationOptions.Routes"/> and posted to the channel the matching route names.
    /// </summary>
    /// <remarks>
    /// Opt-in twice over. Nothing references this package, so no consumer acquires it by installing what
    /// they already had; and once registered it still posts nothing until a Slack bot token and a channel
    /// are configured. Call it after <c>AddThargaAuditLogging</c> — the sink joins the audit fan-out, so
    /// without audit logging there is nothing to notify about.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Slack connection settings and the routing table. Leaving the routes alone keeps the built-ins,
    /// which need only <see cref="NotificationOptions.DefaultChannel"/> to start working.
    /// </param>
    public static IServiceCollection AddThargaSupport(this IServiceCollection services, Action<SupportOptions> configure = null)
    {
        var options = new SupportOptions();
        configure?.Invoke(options);

        // Projected onto the two options types the components consume, so each depends on its own
        // section rather than on the whole module — and so a later section cannot widen what Slack sees.
        services.Configure<SlackOptions>(o =>
        {
            o.BotToken = options.Slack.BotToken;
            o.ApiBaseAddress = options.Slack.ApiBaseAddress;
            o.Timeout = options.Slack.Timeout;
        });
        services.Configure<NotificationOptions>(o =>
        {
            o.DefaultChannel = options.Notifications.DefaultChannel;
            o.Routes = options.Notifications.Routes;
        });

        services.AddHttpClient(SlackClient.HttpClientName);
        services.TryAddSingleton<ISlackClient, SlackClient>();

        // Singleton because CompositeAuditLogger is one, and a scoped sink captured by a singleton is
        // the captive dependency that has already taken this repo's sample down once.
        services.TryAddSingleton<NotificationRouter>();
        services.TryAddSingleton<SlackNotificationSink>();

        // Two registrations of the one instance: the audit fan-out finds it as a logger, the host starts
        // its background pump. Resolving the concrete type in both keeps them the same object — a second
        // instance would queue entries nothing drains.
        services.AddSingleton<IAuditLogger>(sp => sp.GetRequiredService<SlackNotificationSink>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SlackNotificationSink>());

        return services;
    }

    /// <summary>
    /// Registers support cases: a signed-in member can raise a case for their team, reply to it and read its
    /// history, persisted and authorized.
    /// </summary>
    /// <remarks>
    /// <b>Separate from <see cref="AddThargaSupport"/> on purpose.</b> Notifications must be usable without
    /// the case machinery — a product that only wants "post to Slack when a team is created" should not
    /// acquire a case store and a scope pair for it.
    /// <para>
    /// <b>Requires a registered <see cref="ISupportCaseStore"/>.</b> `AddThargaTeamRepository` provides the
    /// MongoDB one when a team repository is registered. Nothing here registers a store, because choosing
    /// storage is the host's decision and this package must not acquire a database dependency.
    /// </para>
    /// <para>
    /// <b>Nothing else may be registered as <see cref="ISupportCaseService"/>.</b> The only registration is
    /// the authorizing decorator; the implementation it wraps is internal and unregistered by itself, so
    /// there is no unchecked instance in the container for a component to resolve by accident.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddThargaSupportCases(this IServiceCollection services, Action<SupportCaseOptions> configure = null)
    {
        var caseOptions = new SupportCaseOptions();
        configure?.Invoke(caseOptions);
        services.Configure<SupportCaseOptions>(o =>
        {
            o.SlackChannel = caseOptions.SlackChannel;
            o.SigningSecret = caseOptions.SigningSecret;
        });

        // Only when a channel is configured. Without it the case service resolves no channel at all, which
        // is the site-only shape slice 1 shipped -- not a degraded version of this one.
        if (!string.IsNullOrWhiteSpace(caseOptions.SlackChannel))
        {
            services.TryAddScoped<ISupportChannel, SlackSupportChannel>();
            services.TryAddScoped<SlackEventHandler>();
        }

        services.TryAddSingleton(TimeProvider.System);

        // Purging a team destroys its cases. The store has exposed DeleteCasesForTeamAsync since the cases
        // shipped; this is the wiring that was missing, because the purge site could not reach the store.
        services.AddTransient<ITeamPurgeParticipant, SupportCasePurgeParticipant>();

        services.AddThargaScopes(scopes =>
        {
            scopes.Register(SupportScopes.Read, AccessLevel.Administrator,
                "Read any support case in the team, not only your own. A case holds whatever a user typed into it.");
            scopes.Register(SupportScopes.Manage, AccessLevel.Administrator,
                "Reply to and close any support case in the team.");
        });

        // Auditing wraps authorization so a refusal is recorded as a failed entry rather than lost, matching
        // how access-level and scope denials are already audited. Composed the other way round, every
        // refused attempt would vanish and nothing would fail to compile.
        services.AddScoped<ISupportCaseService>(sp => new AuditingSupportCaseServiceDecorator(
            new AuthorizationSupportCaseServiceDecorator(
                new SupportCaseService(
                    sp.GetRequiredService<ISupportCaseStore>(),
                    sp.GetRequiredService<TeamAuthorizer>(),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetService<ISupportChannel>()),
                sp.GetRequiredService<TeamAuthorizer>()),
            sp.GetRequiredService<CompositeAuditLogger>(),
            sp.GetRequiredService<IAuditEntryFactory>()));

        return services;
    }
}
