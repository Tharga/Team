using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>Registers automatic notices: the host's catalogue, raising from server code, each user's choices, each team's defaults and delivery in the background.</summary>
public static class UserNoticeRegistration
{
    private const string ManageDescription = "Set what the team's members receive of automatic notices, and how, until they choose for themselves.";

    /// <summary>Registers <see cref="IUserNoticeRaiser"/>, <see cref="IUserNoticePreferenceService"/>, <see cref="IUserNoticeTeamService"/> gated by <see cref="NoticeScopes.Manage"/>, and the background delivery, with the catalogue in <paramref name="configure"/>.</summary>
    /// <remarks>Call it after <see cref="UserMailRegistration.AddThargaUserMail"/>; storage and the member directory come from <c>AddThargaTeamRepository</c>.</remarks>
    /// <exception cref="InvalidOperationException">Mail to users is not registered, a notice is not acceptable, no absolute <see cref="UserMailOptions.BaseUrl"/> is set for the links, or a setting is out of range.</exception>
    public static IServiceCollection AddThargaUserNotices(this IServiceCollection services, Action<UserNoticeOptions> configure = null)
    {
        var categories = services.LastOrDefault(x => x.ServiceType == typeof(UserMailCategoryRegistry))?.ImplementationInstance as UserMailCategoryRegistry
                         ?? throw new InvalidOperationException($"Call {nameof(UserMailRegistration.AddThargaUserMail)} before {nameof(AddThargaUserNotices)}: notices are sent as mail to users, in its categories.");
        var mailOptions = (services.LastOrDefault(x => x.ServiceType == typeof(IOptions<UserMailOptions>))?.ImplementationInstance as IOptions<UserMailOptions>)?.Value ?? new UserMailOptions();

        var options = new UserNoticeOptions();
        configure?.Invoke(options);

        var notices = UserNoticeRegistry.Create(options.Notices, categories);
        Validate(options, notices, mailOptions);

        services.TryAddSingleton(Options.Create(options));
        services.TryAddSingleton(notices);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<UserNoticeSchedule>();

        services.TryAddScoped(sp => new UserNoticeEligibility(sp.GetService<ITenantRoleService>(), sp.GetService<IScopeRegistry>()));
        services.TryAddScoped<IUserNoticeRaiser>(sp => new UserNoticeRaiser(notices, sp.GetRequiredService<IUserNoticeOutbox>(), sp.GetRequiredService<TimeProvider>()));
        services.TryAddScoped<IUserNoticePreferenceService>(sp => new UserNoticePreferenceService(
            notices,
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<UserMailPreferences>(),
            sp.GetRequiredService<IUserNoticePreferenceStore>(),
            sp.GetRequiredService<IUserMessageMemberDirectory>(),
            sp.GetRequiredService<UserNoticeEligibility>(),
            sp.GetRequiredService<TeamAuthorizer>(),
            sp.GetRequiredService<IUserService>(),
            sp.GetService<CompositeAuditLogger>(),
            sp.GetService<IAuditEntryFactory>()));

        if (services.All(x => x.ServiceType != typeof(IUserNoticeTeamService)))
            services.AddTeamService<IUserNoticeTeamService, UserNoticeTeamService>();

        services.TryAddScoped(sp => new UserNoticeMail(notices, sp.GetRequiredService<UserMailText>(), sp.GetRequiredService<IOptions<UserMailOptions>>()));
        services.TryAddScoped(sp => new UserNoticeDistributor(
            sp.GetRequiredService<IUserNoticeOutbox>(),
            notices,
            sp.GetRequiredService<IUserMessageMemberDirectory>(),
            sp.GetRequiredService<IUserNoticePreferenceStore>(),
            sp.GetRequiredService<UserNoticeEligibility>(),
            sp.GetRequiredService<IUserMailQueue>(),
            sp.GetRequiredService<UserNoticeMail>(),
            sp.GetRequiredService<UserNoticeSchedule>(),
            sp,
            sp.GetRequiredService<IOptions<UserNoticeOptions>>(),
            sp.GetRequiredService<IOptions<UserMailOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<UserNoticeDistributor>>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, UserNoticeSender>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<ITeamPurgeParticipant, UserNoticePurgeParticipant>());

        services.AddThargaScopes(scopes =>
        {
            if (scopes.All.Any(x => x.Name == NoticeScopes.Manage)) return;

            if (options.ManageAccessLevel is { } level) scopes.Register(NoticeScopes.Manage, level, ManageDescription);
            else scopes.RegisterGrantOnly(NoticeScopes.Manage, ManageDescription);
        });

        return services;
    }

    private static void Validate(UserNoticeOptions options, UserNoticeRegistry notices, UserMailOptions mailOptions)
    {
        _ = UserNoticeSchedule.Zone(options.TimeZoneId);

        if (notices.All.Count > 0 && !(Uri.TryCreate(mailOptions.BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            throw new InvalidOperationException($"Notices link to what they are about, so set {nameof(UserMailOptions)}.{nameof(UserMailOptions.BaseUrl)} to the site's absolute address.");

        if (options.BurstWindow < TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(UserNoticeOptions)}.{nameof(UserNoticeOptions.BurstWindow)} cannot be negative.");

        if (options.MaxItemsPerMail < 1)
            throw new InvalidOperationException($"{nameof(UserNoticeOptions)}.{nameof(UserNoticeOptions.MaxItemsPerMail)} must be at least 1.");

        if (options.MaxAttempts < 1)
            throw new InvalidOperationException($"{nameof(UserNoticeOptions)}.{nameof(UserNoticeOptions.MaxAttempts)} must be at least 1.");

        if (options.Lease <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(UserNoticeOptions)}.{nameof(UserNoticeOptions.Lease)} must be longer than zero.");
    }
}
