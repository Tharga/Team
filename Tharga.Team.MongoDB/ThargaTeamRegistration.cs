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