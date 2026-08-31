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

        // Purging a team destroys its icons too. Hygiene rather than security -- an orphaned icon grants
        // nothing -- but one seam should cover every per-team store, not the ones somebody remembered.
        services.AddTransient<ITeamPurgeParticipant, IconPurgeParticipant>();

        if (o._userEntity != null)
        {
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

            // Deduplicates inbound channel events. Shared across instances by virtue of being in the same
            // database as the cases, with a unique index making the record-or-refuse decision atomic.
            services.TryAddSingleton(TimeProvider.System);
            services.AddTransient<ISupportEventLedgerCollection, SupportEventLedgerCollection>();
            services.TrackMongoCollection(typeof(ISupportEventLedgerCollection), typeof(SupportEventLedgerCollection));
            services.TryAddScoped<ISupportEventLedger, MongoSupportEventLedger>();

            // Reports members stored with no access level, which are silently being treated as Owner.
            // Registered only alongside a team repository, because without one there is nothing to read.
            if (o.CheckMemberAccessLevels)
            {
                var checkType = typeof(AccessLevelCompletenessCheck<,>).MakeGenericType(teamEntityType, teamMemberModelType);
                services.AddSingleton(typeof(IHostedService), checkType);
            }
        }
    }
}