using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

public static class ThargaTeamRegistration
{
    public static void AddThargaTeamRepository(this IServiceCollection services, Action<ThargaTeamOptions> options = default)
    {
        var o = new ThargaTeamOptions();
        options?.Invoke(o);

        services.AddSingleton(Options.Create(o));

        // Built-in icon store (independent of user/team registration). TryAdd so a consumer store set via
        // o.AddIconStore<T>() wins; AddOptions ensures IOptions<IconOptions> resolves even without the
        // Blazor platform configuring it.
        services.AddOptions<IconOptions>();
        services.TryAddScoped<IIconProcessor, NoOpIconProcessor>();
        services.AddTransient<IIconRepositoryCollection, IconRepositoryCollection>();
        services.TrackMongoCollection(typeof(IIconRepositoryCollection), typeof(IconRepositoryCollection));
        services.TryAddScoped<IIconStore, MongoIconStore>();

        // Site-level icon settings, persisted so they survive a restart. TryAdd for the same reason as above.
        services.AddTransient<IIconSettingsRepositoryCollection, IconSettingsRepositoryCollection>();
        services.TrackMongoCollection(typeof(IIconSettingsRepositoryCollection), typeof(IconSettingsRepositoryCollection));
        services.TryAddScoped<IIconSettingsStore, MongoIconSettingsStore>();

        // Purging a team destroys its icons too. Hygiene rather than security -- an orphaned icon grants
        // nothing -- but one seam should cover every per-team store, not the ones somebody remembered.
        services.AddTransient<ITeamPurgeParticipant, IconPurgeParticipant>();

        // Mail to users: each user's choices, and the queue the sender drains. Neither depends on the user or team
        // entity types. TryAdd so a host keeping them elsewhere wins.
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<IUserMailPreferenceCollection, UserMailPreferenceCollection>();
        services.TrackMongoCollection(typeof(IUserMailPreferenceCollection), typeof(UserMailPreferenceCollection));
        services.TryAddScoped<IUserMailPreferenceStore, MongoUserMailPreferenceStore>();
        services.AddTransient<IUserMailCollection, UserMailCollection>();
        services.TrackMongoCollection(typeof(IUserMailCollection), typeof(UserMailCollection));
        services.AddTransient<IUserMailBatchCollection, UserMailBatchCollection>();
        services.TrackMongoCollection(typeof(IUserMailBatchCollection), typeof(UserMailBatchCollection));
        services.TryAddScoped<IUserMailOutbox, MongoUserMailOutbox>();

        // The log of messages staff sent to users; TryAdd so a host keeping it elsewhere wins.
        services.AddTransient<IUserMessageCollection, UserMessageCollection>();
        services.TrackMongoCollection(typeof(IUserMessageCollection), typeof(UserMessageCollection));
        services.TryAddScoped<IUserMessageLog, MongoUserMessageLog>();

        // Addresses that opted out of contact mail, kept for good, and the log of messages to contacts; TryAdd so a host
        // keeping them elsewhere wins.
        services.AddTransient<IContactMailSuppressionCollection, ContactMailSuppressionCollection>();
        services.TrackMongoCollection(typeof(IContactMailSuppressionCollection), typeof(ContactMailSuppressionCollection));
        services.TryAddScoped<IContactMailSuppressionStore, MongoContactMailSuppressionStore>();
        services.AddTransient<IContactMessageCollection, ContactMessageCollection>();
        services.TrackMongoCollection(typeof(IContactMessageCollection), typeof(ContactMessageCollection));
        services.TryAddScoped<IContactMessageLog, MongoContactMessageLog>();

        // Automatic notices: occurrences until distributed, each user's notices and streams until mailed, and the
        // choices and team defaults that differ from a default. TryAdd so a host keeping them elsewhere wins.
        services.AddTransient<IUserNoticeCollection, UserNoticeCollection>();
        services.TrackMongoCollection(typeof(IUserNoticeCollection), typeof(UserNoticeCollection));
        services.AddTransient<IUserNoticeItemCollection, UserNoticeItemCollection>();
        services.TrackMongoCollection(typeof(IUserNoticeItemCollection), typeof(UserNoticeItemCollection));
        services.AddTransient<IUserNoticeStreamCollection, UserNoticeStreamCollection>();
        services.TrackMongoCollection(typeof(IUserNoticeStreamCollection), typeof(UserNoticeStreamCollection));
        services.TryAddScoped<IUserNoticeOutbox, MongoUserNoticeOutbox>();
        services.AddTransient<IUserNoticePreferenceCollection, UserNoticePreferenceCollection>();
        services.TrackMongoCollection(typeof(IUserNoticePreferenceCollection), typeof(UserNoticePreferenceCollection));
        services.AddTransient<IUserNoticeTeamDefaultCollection, UserNoticeTeamDefaultCollection>();
        services.TrackMongoCollection(typeof(IUserNoticeTeamDefaultCollection), typeof(UserNoticeTeamDefaultCollection));
        services.TryAddScoped<IUserNoticePreferenceStore, MongoUserNoticePreferenceStore>();

        if (o._userEntity != null)
        {
            // Where a user mail goes, read from the user record when it is sent.
            services.TryAddScoped(typeof(IUserMailRecipientDirectory), typeof(MongoUserMailRecipientDirectory<>).MakeGenericType(o._userEntity));

            var userEntityType = o._userEntity;

            var userRepositoryInterfaceType = typeof(IUserRepository<>).MakeGenericType(userEntityType);
            var userRepositoryImplementationType = typeof(UserRepository<>).MakeGenericType(userEntityType);

            var userRepositoryCollectionInterfaceType = typeof(IUserRepositoryCollection<>).MakeGenericType(userEntityType);
            var userRepositoryCollectionImplementationType = o._userCollectionType
                ?? typeof(UserRepositoryCollection<>).MakeGenericType(userEntityType);

            services.AddTransient(userRepositoryInterfaceType, userRepositoryImplementationType);
            services.AddTransient(userRepositoryCollectionInterfaceType, userRepositoryCollectionImplementationType);
            services.TrackMongoCollection(userRepositoryCollectionInterfaceType, userRepositoryCollectionImplementationType);
        }

        if (o._teamEntity != null && o._teamMemberModel != null)
        {
            var teamEntityType = o._teamEntity;
            var teamMemberModelType = o._teamMemberModel;

            var teamRepositoryInterfaceType = typeof(ITeamRepository<,>).MakeGenericType(teamEntityType, teamMemberModelType);
            var teamRepositoryImplementationType = typeof(TeamRepository<,>).MakeGenericType(teamEntityType, teamMemberModelType);

            var teamRepositoryCollectionInterfaceType = typeof(ITeamRepositoryCollection<,>).MakeGenericType(teamEntityType, teamMemberModelType);
            var teamRepositoryCollectionImplementationType = typeof(TeamRepositoryCollection<,>).MakeGenericType(teamEntityType, teamMemberModelType);

            services.AddTransient(teamRepositoryInterfaceType, teamRepositoryImplementationType);
            services.AddTransient(teamRepositoryCollectionInterfaceType, teamRepositoryCollectionImplementationType);
            services.TrackMongoCollection(teamRepositoryCollectionInterfaceType, teamRepositoryCollectionImplementationType);

            // Support cases. Registered alongside the team repository because a case belongs to a team, and
            // TryAdd so a host substituting its own store wins.
            services.AddTransient<ISupportCaseRepositoryCollection, SupportCaseRepositoryCollection>();
            services.TrackMongoCollection(typeof(ISupportCaseRepositoryCollection), typeof(SupportCaseRepositoryCollection));
            services.TryAddScoped<ISupportCaseStore, MongoSupportCaseStore>();

            // The files support messages carry, in GridFS beside the cases rather than inside them. TryAdd so a
            // host keeping files in blob storage wins.
            services.TryAddScoped<ISupportAttachmentStore>(sp => new MongoSupportAttachmentStore(
                new GridFsSupportAttachmentBucket(sp.GetRequiredService<IMongoDbServiceFactory>(), sp.GetService<IOptions<ThargaTeamOptions>>())));

            // Deduplicates inbound channel events. Shared across instances by virtue of being in the same
            // database as the cases, with a unique index making the record-or-refuse decision atomic.
            services.TryAddSingleton(TimeProvider.System);
            services.AddTransient<ISupportEventLedgerCollection, SupportEventLedgerCollection>();
            services.TrackMongoCollection(typeof(ISupportEventLedgerCollection), typeof(SupportEventLedgerCollection));
            services.TryAddScoped<ISupportEventLedger, MongoSupportEventLedger>();

            // How far this deployment has read the support mailbox. A bookmark rather than a record: losing
            // it costs a re-read, which the ledger above makes harmless -- which is why the port is
            // optional and the poller falls back to keeping its position in memory.
            services.AddTransient<ISupportMailPositionCollection, SupportMailPositionCollection>();
            services.TrackMongoCollection(typeof(ISupportMailPositionCollection), typeof(SupportMailPositionCollection));
            services.TryAddScoped<ISupportMailPositionStore, MongoSupportMailPositionStore>();

            // Team mailboxes: each team's keys in one document, so a rotation is one write; received mail with a
            // unique id per delivery, so reading the shared mailbox twice keeps each mail once. TryAdd so a host
            // substituting its own stores wins.
            services.AddTransient<ITeamMailKeysCollection, TeamMailKeysCollection>();
            services.TrackMongoCollection(typeof(ITeamMailKeysCollection), typeof(TeamMailKeysCollection));
            services.TryAddScoped<ITeamMailKeyStore, MongoTeamMailKeyStore>();

            // Each team's AI connection for the support assistant, one document per team. TryAdd so a host keeping
            // its own wins.
            services.AddTransient<ISupportAiConnectionCollection, SupportAiConnectionCollection>();
            services.TrackMongoCollection(typeof(ISupportAiConnectionCollection), typeof(SupportAiConnectionCollection));
            services.TryAddScoped<ISupportAiConnectionStore, MongoSupportAiConnectionStore>();
            services.AddTransient<IInboundMailCollection, InboundMailCollection>();
            services.TrackMongoCollection(typeof(IInboundMailCollection), typeof(InboundMailCollection));
            services.TryAddScoped<ITeamMailStore, MongoTeamMailStore>();

            // Opens both collections at startup. Slack allows three seconds for an inbound event and the
            // first one after a restart took 3.4 s, all of it creating the ledger and assuring its indexes.
            services.AddSingleton<IHostedService, SupportCollectionWarmUp>();

            // Reports a replacement repository that cannot resolve short invitation links (Tharga/Team#286).
            services.AddSingleton(typeof(IHostedService), typeof(InvitationRepositoryCheck<,>).MakeGenericType(teamEntityType, teamMemberModelType));

            // Reports members stored with no access level, who since 4.0 have no access until a level is set.
            // Registered only alongside a team repository, because without one there is nothing to read.
            if (o.CheckMemberAccessLevels)
            {
                var checkType = typeof(AccessLevelCompletenessCheck<,>).MakeGenericType(teamEntityType, teamMemberModelType);
                services.AddSingleton(typeof(IHostedService), checkType);
            }
        }

        // Who a support mail's sender is. Needs both stores; without them support mail opens unassigned as it
        // always did. TryAdd so a host substituting its own wins.
        if (o._userEntity != null && o._teamEntity != null && o._teamMemberModel != null)
        {
            var directoryType = typeof(MongoSupportSenderDirectory<,,>).MakeGenericType(o._userEntity, o._teamEntity, o._teamMemberModel);
            services.TryAddScoped(typeof(ISupportSenderDirectory), directoryType);

            // Who a staff message or a notice may reach: the active members of the teams it is written or raised in.
            services.TryAddScoped(typeof(IUserMessageMemberDirectory), typeof(MongoUserMessageMemberDirectory<,,>).MakeGenericType(o._userEntity, o._teamEntity, o._teamMemberModel));
        }
    }
}