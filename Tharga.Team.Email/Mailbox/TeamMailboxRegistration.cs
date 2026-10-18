using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Registers a mailbox for every team: an opaque, rotatable address per team, behind the team scopes it declares.
/// </summary>
public static class TeamMailboxRegistration
{
    private const string ReadDescription = "List and open the team's received mail, and see the team's mail addresses.";
    private const string HandleDescription = "Record what became of a received mail and what it belongs to.";
    private const string ManageDescription = "Issue and rotate the team's mail address. Rotating changes the address every sender knows; the old one keeps working until it retires.";
    private const string PostmasterReadDescription = "List and open mail that reached the shared mailbox at an address no team owns. It may hold any team's material, so every open is audited.";
    private const string PostmasterManageDescription = "Route a mail the postmaster holds to a team, which delivers it there marked as rerouted, or archive it without routing it and restore it.";

    /// <summary>
    /// Registers <see cref="ITeamMailboxService"/> and its scopes, and reads the catch-all mailbox when
    /// <see cref="TeamMailboxOptions.Mail"/> has IMAP settings. A persistence adapter supplies
    /// <see cref="ITeamMailKeyStore"/> and <see cref="ITeamMailStore"/> — <c>Tharga.Team.MongoDB</c> registers both — and the
    /// host supplies <see cref="IInboundMailContentStore"/> to keep what arrives.
    /// </summary>
    /// <exception cref="InvalidOperationException">No <see cref="TeamMailboxOptions.Domain"/> is configured.</exception>
    public static IServiceCollection AddThargaTeamMailbox(this IServiceCollection services, Action<TeamMailboxOptions> configure)
    {
        var options = new TeamMailboxOptions();
        configure?.Invoke(options);
        if (string.IsNullOrWhiteSpace(options.Domain)) throw new InvalidOperationException($"{nameof(TeamMailboxOptions)}.{nameof(TeamMailboxOptions.Domain)} must be set: it is the domain every team address is on.");
        _ = MailKeyGenerator.New(options.KeyLength);

        services.Configure<TeamMailboxOptions>(x =>
        {
            x.Domain = options.Domain;
            x.Suffix = options.Suffix ?? string.Empty;
            x.KeyLength = options.KeyLength;
            x.RetirementPeriod = options.RetirementPeriod;
            x.PostmasterRetention = options.PostmasterRetention;
            x.ReadAccessLevel = options.ReadAccessLevel;
            x.HandleAccessLevel = options.HandleAccessLevel;
            x.ManageAccessLevel = options.ManageAccessLevel;
            x.Mail = options.Mail ?? new MailOptions();
        });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITeamMailNotifier, TeamMailNotifier>();
        services.TryAddSingleton<ITeamMailboxHealth, TeamMailboxHealth>();
        services.AddTeamService<ITeamMailboxService, TeamMailboxService>();
        services.TryAddScoped<ITeamMailboxProvisioning, TeamMailboxService>();
        services.AddSystemService<IPostmasterService, PostmasterService>();
        services.AddTransient<ITeamPurgeParticipant, TeamMailboxPurgeParticipant>();

        if (options.Mail?.Imap?.IsConfigured == true)
        {
            services.TryAddScoped<IMailboxSource, MailClientMailboxSource>();
            services.TryAddScoped<TeamMailReceiver>();
            services.AddHostedService<TeamMailboxPoller>();
            services.AddHostedService<PostmasterRetentionSweep>();
        }

        services.AddThargaScopes(scopes =>
        {
            RegisterTeamScope(scopes, MailboxScopes.Read, options.ReadAccessLevel, ReadDescription);
            RegisterTeamScope(scopes, MailboxScopes.Handle, options.HandleAccessLevel, HandleDescription);
            RegisterTeamScope(scopes, MailboxScopes.Manage, options.ManageAccessLevel, ManageDescription);
        });

        services.AddThargaSystemScopes(scopes =>
        {
            scopes.Register(SystemMailboxScopes.PostmasterRead, PostmasterReadDescription);
            scopes.Register(SystemMailboxScopes.PostmasterManage, PostmasterManageDescription);
        });

        return services;
    }

    private static void RegisterTeamScope(ScopeRegistry scopes, string scope, AccessLevel? level, string description)
    {
        if (level is { } accessLevel) scopes.Register(scope, accessLevel, description);
        else scopes.RegisterGrantOnly(scope, description);
    }
}
