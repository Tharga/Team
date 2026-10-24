using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;
using Tharga.Team.Service.Email;
using Tharga.Team.Email;
using Tharga.Team.Support.Notifications;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support;

/// <summary>
/// Registration for the support module.
/// </summary>
public static class SupportRegistration
{
    private const string SupportReadDescription =
        "Read any support case in the team, not only your own. A case holds whatever a user typed into it.";

    private const string SupportManageDescription =
        "Reply to and close any support case in the team.";

    private const string AssistantManageDescription =
        "Choose what the support assistant answers the team with -- the platform's model, the team's own AI provider, or nothing -- and store the team's key for that provider.";

    /// <summary>
    /// Lets each team choose what the support assistant answers it with: the platform's model, its own provider, or
    /// nothing, through <see cref="ISupportAiConnectionService"/> and its component.
    /// </summary>
    /// <remarks>
    /// <b>A team that never chose keeps the platform's model</b>, so calling this changes nothing until somebody chooses.
    /// Call it with <see cref="AddThargaSupportCases"/>, before or after.
    /// <list type="bullet">
    /// <item>The store comes from <c>AddThargaTeamRepository</c> in <c>Tharga.Team.MongoDB</c>.</item>
    /// <item>Providers come from an <see cref="ISupportChatClientFactory"/>, such as <c>AddThargaSupportAnthropic</c>
    /// for Neurolito. Without one, a team chooses between the platform's model and off.</item>
    /// <item>Keys are encrypted with Data Protection. On more than one instance, or where the file system does not
    /// survive a restart, persist the key ring where every instance reads it, such as
    /// <c>AddDataProtection().PersistKeysToThargaMongo()</c>, or stored keys become unreadable.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddThargaSupportAiConnections(this IServiceCollection services, Action<SupportAiConnectionOptions> configure = null)
    {
        if (services.Any(x => x.ServiceType == typeof(SupportAiConnectionOptions))) return services;

        var options = new SupportAiConnectionOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SupportAiClientCache>();

        // Replaces the default resolver whichever order the two calls come in: removed here if it is already there,
        // and AddThargaSupportCases only tries to add it, so a later call leaves this one in place.
        services.RemoveAll<ISupportChatClientResolver>();
        services.AddScoped<ISupportChatClientResolver>(sp => new StoredChatClientResolver(
            sp.GetRequiredService<ISupportAiConnectionStore>(),
            sp.GetRequiredService<IDataProtectionProvider>(),
            sp.GetRequiredService<SupportAiClientCache>(),
            sp.GetRequiredService<ILogger<StoredChatClientResolver>>(),
            sp.GetService<IChatClient>(),
            sp.GetService<ISupportChatClientFactory>()));

        services.AddScoped<ISupportAiConnectionService>(sp => new AuditingSupportAiConnectionServiceDecorator(
            new AuthorizationSupportAiConnectionServiceDecorator(
                new SupportAiConnectionService(
                    sp.GetRequiredService<ISupportAiConnectionStore>(),
                    sp.GetRequiredService<IDataProtectionProvider>(),
                    sp.GetRequiredService<TeamAuthorizer>(),
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetService<IChatClient>() != null,
                    sp.GetService<ISupportChatClientFactory>()),
                sp.GetRequiredService<TeamAuthorizer>()),
            sp.GetRequiredService<CompositeAuditLogger>(),
            sp.GetRequiredService<IAuditEntryFactory>()));

        services.AddTransient<ITeamPurgeParticipant, SupportAiConnectionPurgeParticipant>();

        services.AddThargaScopes(scopes => RegisterTeamScope(scopes, SupportScopes.AssistantManage, options.ScopeAccessLevel, AssistantManageDescription));

        return services;
    }

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
    /// <b>Mail from a verified sender in one team opens in that team when an <see cref="ISupportSenderDirectory"/>
    /// is registered</b> — <c>AddThargaTeamRepository</c> registers one when it has both the user and the team
    /// repository. Without one, every new mail opens unassigned, as before.
    /// </para>
    /// <para>
    /// <b>Files are kept through an <see cref="ISupportAttachmentStore"/></b> — <c>AddThargaTeamRepository</c>
    /// registers the GridFS one. Without one, a mail's files are named in its message as not stored and a reply
    /// carrying files is refused.
    /// </para>
    /// <para>
    /// <b>Further mailboxes are added with <see cref="SupportCaseOptions.AddMailbox"/></b>; each is read on its own and answers the cases that came in through it.
    /// </para>
    /// <para>
    /// <b>An answer mail names no staff member</b>: <see cref="SupportCaseOptions.Answers"/> sets the name earlier support entries are attributed to, the signature and the time zone of quoted dates.
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
            o.TeamScopeAccessLevel = caseOptions.TeamScopeAccessLevel;
            o.Visibility = caseOptions.Visibility;
            o.DefaultVisibility = caseOptions.DefaultVisibility;
            o.AssistantDefaultVisibility = caseOptions.AssistantDefaultVisibility;
            o.StaffCloseAfter = caseOptions.StaffCloseAfter;
            CopySettable(caseOptions.Answers, o.Answers);
        });

        // Projected onto its own options type for the same reason the Slack section is: the mail transport
        // depends on what it needs rather than on the whole module, and a later section cannot widen it.
        services.Configure<MailOptions>(o => CopyMail(caseOptions.Email, o));

        RequireSendingAddressIsAccepted(caseOptions.Email, SupportCaseOptions.PrimaryMailboxName);

        SupportMailbox[] additionalMailboxes = [.. caseOptions.AdditionalMailboxes.Select(x => new SupportMailbox(x.Name, CopyOf(x.Options)))];
        foreach (var mailbox in additionalMailboxes) RequireSendingAddressIsAccepted(mailbox.Options, mailbox.Name);

        services.TryAddSingleton(sp => new SupportMailboxes(sp.GetRequiredService<IOptions<MailOptions>>().Value, additionalMailboxes));
        services.TryAddSingleton<ISupportMailboxHealth, SupportMailboxHealthCheck>();

        _ = SupportAnswerFrame.Zone(caseOptions.Answers.TimeZoneId);
        services.TryAddSingleton(caseOptions.Answers);

        MailOptions[] everyMailbox = [caseOptions.Email, .. additionalMailboxes.Select(x => x.Options)];

        // Mail, when a mailbox is configured. Dormant otherwise: a host that has not set a server resolves
        // no channel and runs no poller, which is the site-only shape rather than a broken email one.
        //
        // TryAddEnumerable, so email and Slack are both live -- a case holds a binding facing the customer
        // and one facing support at the same time.
        if (everyMailbox.Any(x => x.Imap.IsConfigured || x.Smtp.IsConfigured))
        {
            // The transport is wrapped rather than changed, so it stays ignorant of Team types (Tharga/Team#290).
            services.AddOutboundMailPolicy();
            services.AddMailTemplate();
            services.TryAddScoped<MailClient>();
            services.TryAddScoped<IMailClient>(sp => new PolicyGovernedMailClient(
                sp.GetRequiredService<MailClient>(),
                sp.GetRequiredService<IOutboundMailPolicy>(),
                sp.GetService<ILogger<PolicyGovernedMailClient>>()));
            services.TryAddScoped<ISupportMailClients, SupportMailClients>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<ISupportChannel, EmailSupportChannel>());
        }

        // Only with a mailbox to read. Sending without reading is a legitimate configuration -- a host that
        // mails replies out and takes them in on the site -- and it must not start a poller against a
        // server that was never configured.
        if (everyMailbox.Any(x => x.Imap.IsConfigured))
        {
            services.TryAddScoped<EmailEventHandler>();
            services.AddSingleton<IHostedService, SupportMailPoller>();
        }

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
            // TryAddEnumerable rather than TryAdd: a case can hold a binding per channel, so registering
            // email must not mean Slack loses. It still refuses a second registration of this same
            // implementation, which is what TryAdd was here for.
            services.TryAddEnumerable(ServiceDescriptor.Scoped<ISupportChannel, SlackSupportChannel>());
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
        services.TryAddSingleton<ISupportTypingNotifier, SupportTypingNotifier>();

        // Purging a team destroys its cases. The store has exposed DeleteCasesForTeamAsync since the cases
        // shipped; this is the wiring that was missing, because the purge site could not reach the store.
        services.AddTransient<ITeamPurgeParticipant, SupportCasePurgeParticipant>();

        // The level is the host's to choose, because whether a team administrator should read their members'
        // conversations is a product decision rather than a library one. The default is what every host had
        // before the option existed.
        services.AddThargaScopes(scopes =>
        {
            RegisterTeamScope(scopes, SupportScopes.Read, caseOptions.TeamScopeAccessLevel, SupportReadDescription);
            RegisterTeamScope(scopes, SupportScopes.Manage, caseOptions.TeamScopeAccessLevel, SupportManageDescription);
        });

        // The unassigned queue, and answering across every team. Registered here rather than in the Blazor
        // platform because these scopes only mean anything where support cases exist, and a catalogue entry
        // for a capability the host has not registered is an offer it cannot honour.
        services.AddThargaSystemScopes(scopes =>
        {
            scopes.Register(SystemSupportScopes.Read,
                "Read and list support cases that belong to no team -- inbound mail from a sender whose team could not be determined. A team scope cannot govern these, because there is no team to hold it against.");
            scopes.Register(SystemSupportScopes.Manage,
                "Reply to, close, reopen and assign a support case that belongs to no team. Assigning decides which tenant the case and its whole transcript become part of.");
            scopes.Register(SystemSupportScopes.AllRead,
                "Read and list support cases in any team, without belonging to it -- for the people who run support, who are members of none of the customers' teams. This reaches every conversation every customer has had.");
            scopes.Register(SystemSupportScopes.AllManage,
                "Reply to, close, reopen and hand over a support case in any team, without belonging to it. Satisfies a read as well. Does not grant assignment, which decides which tenant an unassigned case joins and stays with the unassigned queue.");
        });

        // The assistant, and only when the host has given it something to talk to. An IChatClient is the
        // host's to register -- a local model, a self-hosted one, or a hosted API -- so the toolkit names no
        // provider and takes no vendor package. With none registered the factory yields null, the service's
        // optional responder stays null, and every path behaves exactly as it did before assistants existed.
        //
        // Resolved rather than injected into a component: an optional dependency taken as a required
        // injection is Tharga/Team#266, where opening a dialog killed the circuit because nothing had
        // registered what it demanded.
        //
        // Which model answers is the resolver's to say, per team. The default hands everybody the registered
        // IChatClient; a host that registers its own resolver, before or after this call, has an assistant
        // even with no IChatClient of its own, because the resolver is then where the models come from.
        services.AddSingleton(caseOptions.Assistant);
        services.TryAddScoped<ISupportAssistantTools, SupportAssistantTools>();
        services.TryAddScoped<ISupportChatClientResolver>(sp => new RegisteredChatClientResolver(sp.GetService<IChatClient>()));
        services.TryAddScoped<ISupportResponder>(sp =>
        {
            var resolver = sp.GetRequiredService<ISupportChatClientResolver>();
            if (resolver is RegisteredChatClientResolver && sp.GetService<IChatClient>() == null) return null;

            return new ChatSupportResponder(
                resolver,
                sp.GetRequiredService<ISupportAssistantTools>(),
                sp.GetRequiredService<SupportAssistantOptions>(),
                sp.GetRequiredService<ILogger<ChatSupportResponder>>());
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
                    sp.GetServices<ISupportChannel>(),
                    sp.GetRequiredService<ISupportCaseNotifier>(),
                    sp.GetService<ISupportResponder>(),
                    caseOptions.Visibility,
                    sp.GetService<ITeamDirectoryService>(),
                    sp.GetService<IUserService>(),
                    caseOptions.DefaultVisibility,
                    caseOptions.AssistantDefaultVisibility,
                    sp.GetService<ISupportAttachmentStore>(),
                    sp.GetService<SupportMailboxes>()?.AnswerLimits() ?? SupportAttachmentLimitCheck.LimitsOf(sp.GetService<IOptions<MailOptions>>()?.Value),
                    sp.GetService<ISupportTypingNotifier>()),
                sp.GetRequiredService<TeamAuthorizer>(),
                caseOptions.Visibility,
                caseOptions.StaffCloseAfter,
                sp.GetRequiredService<TimeProvider>()),
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

    private static MailOptions CopyOf(MailOptions from)
    {
        var to = new MailOptions();
        CopyMail(from, to);
        return to;
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
    private static void RequireSendingAddressIsAccepted(MailOptions email, string mailboxName)
    {
        var filter = new RecipientFilter(email.Recipients);

        if (filter.AcceptsEverything || string.IsNullOrWhiteSpace(email.FromAddress)) return;
        if (filter.Accepts(email.FromAddress)) return;

        throw new InvalidOperationException(
            $"The support mailbox '{mailboxName}' is configured to send from '{email.FromAddress}', which its own recipient filter " +
            $"({string.Join(", ", email.Recipients)}) does not accept. Every reply would be discarded. Add the " +
            "address or its domain to the filter, or send from an address the filter already covers.");
    }

    /// <summary>
    /// Registers a team support scope at the level the host chose, or grant-only when it chose none.
    /// </summary>
    /// <remarks>
    /// One place expressing the choice, rather than the same decision written twice. The scope is
    /// registered either way — grant-only is a registration, so the catalogue entry, the description and
    /// the enforcement are unchanged and only the automatic grant goes away.
    /// </remarks>
    private static void RegisterTeamScope(ScopeRegistry scopes, string scope, AccessLevel? level, string description)
    {
        if (level is { } accessLevel)
            scopes.Register(scope, accessLevel, description);
        else
            scopes.RegisterGrantOnly(scope, description);
    }
}
