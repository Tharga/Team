using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Email;
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
            o.UseSubject = caseOptions.UseSubject;
            o.AutoCloseAfter = caseOptions.AutoCloseAfter;
            o.AutoCloseSweepInterval = caseOptions.AutoCloseSweepInterval;
            o.AutoCloseBatchSize = caseOptions.AutoCloseBatchSize;
        });

        // Projected onto its own options type for the same reason the Slack section is: the mail transport
        // depends on what it needs rather than on the whole module, and a later section cannot widen it.
        services.Configure<MailOptions>(o => CopyMail(caseOptions.Email, o));

        RequireSendingAddressIsAccepted(caseOptions.Email);

        // Only when the feature is on. Zero means a host that does not want cases closing themselves runs no
        // timer and no query on its behalf, rather than a sweep that finds nothing every hour.
        if (caseOptions.AutoCloseAfter > TimeSpan.Zero)
        {
            services.TryAddScoped<SupportCaseInactivitySweep>();
            services.AddSingleton<IHostedService, SupportCaseInactivityService>();
        }

        // Only when a channel is configured. Without it the case service resolves no channel at all, which
        // is the site-only shape slice 1 shipped -- not a degraded version of this one.
        if (!string.IsNullOrWhiteSpace(caseOptions.SlackChannel))
        {
            services.TryAddScoped<ISupportChannel, SlackSupportChannel>();
            services.TryAddScoped<SlackEventHandler>();

            // Singleton, because the caches are the point: a scoped presence service would ask Slack about
            // the whole support channel once per request, which is how a deployment gets rate-limited.
            // Registered only with a channel, so a host without Slack resolves nothing and its components
            // render no presence at all rather than "offline".
            services.TryAddSingleton<ISupportPresence, SlackSupportPresence>();
        }

        services.TryAddSingleton(TimeProvider.System);

        // Singleton, and it has to be: an inbound reply is handled in the poller's or the endpoint's own
        // scope while the page waiting for it lives in a circuit's. A scoped notifier would raise the event
        // on an instance nothing is listening to.
        services.TryAddSingleton<ISupportCaseNotifier, SupportCaseNotifier>();

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
                    sp.GetService<ISupportChannel>(),
                    sp.GetRequiredService<ISupportCaseNotifier>()),
                sp.GetRequiredService<TeamAuthorizer>()),
            sp.GetRequiredService<CompositeAuditLogger>(),
            sp.GetRequiredService<IAuditEntryFactory>()));

        return services;
    }

    /// <summary>
    /// Forwards the configured mail settings onto the options type the transport resolves.
    /// </summary>
    /// <remarks>
    /// <b>Copied by reflection rather than as a list of assignments</b>, because a named list is how options
    /// quietly stop working: it is written against the properties that exist that day, one is added later,
    /// and it is then accepted from the host and silently discarded — the setting looks configured and does
    /// nothing. That has already shipped twice in this repository (Tharga/Team#177), which is why
    /// <c>Tharga.Team.Blazor</c> has <c>OptionsForwarder</c> for the same job. This is not that type only
    /// because it is internal to that assembly; if a third caller appears, promote one of them rather than
    /// writing a third.
    /// <para>
    /// The two server sections are named because they are read-only properties holding their own object, so
    /// there is no setter to forward. A <i>third</i> section is a visible structural change to
    /// <see cref="MailOptions"/>, unlike a scalar, and <c>EverySettableMailOption_IsForwarded</c> fails until
    /// it is handled here.
    /// </para>
    /// </remarks>
    private static void CopyMail(MailOptions from, MailOptions to)
    {
        CopySettable(from, to);
        CopySettable(from.Imap, to.Imap);
        CopySettable(from.Smtp, to.Smtp);
    }

    private static void CopySettable<T>(T from, T to) where T : class
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(x => x.CanRead && x.SetMethod?.IsPublic == true))
        {
            property.SetValue(to, property.GetValue(from));
        }
    }

    /// <summary>
    /// Refuses a configuration whose own sending address the recipient filter would reject.
    /// </summary>
    /// <remarks>
    /// <b>The failure this prevents is silent and expensive.</b> Every reply to mail the toolkit sent comes
    /// back addressed to <see cref="MailOptions.FromAddress"/>; if the filter does not accept that address the
    /// poller discards all of them, and the symptom is a mailbox that appears not to be read at all. Nothing
    /// throws, nothing logs an error, and the configuration looks reasonable.
    /// <para>
    /// Checked at registration rather than on the first poll, so it fails at startup with the two values in
    /// the message instead of an hour later with none.
    /// </para>
    /// </remarks>
    private static void RequireSendingAddressIsAccepted(MailOptions email)
    {
        var filter = new RecipientFilter(email.Recipients);

        if (filter.AcceptsEverything || string.IsNullOrWhiteSpace(email.FromAddress)) return;
        if (filter.Accepts(email.FromAddress)) return;

        throw new InvalidOperationException(
            $"Support email is configured to send from '{email.FromAddress}', which its own recipient filter " +
            $"({string.Join(", ", email.Recipients)}) does not accept. Every reply would be discarded. Add the " +
            "address or its domain to the filter, or send from an address the filter already covers.");
    }
}
