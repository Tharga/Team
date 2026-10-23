using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Invitations;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Users;

/// <summary>Registers mail to users: categories, each user's choices, unsubscribe links, and a persisted queue sent in the background.</summary>
public static class UserMailRegistration
{
    /// <summary>Registers <see cref="IUserMailPreferenceService"/>, <see cref="IUserMailHistoryService"/>, <see cref="IUserMailQueue"/> and the background sender, with the categories and settings in <paramref name="configure"/>.</summary>
    /// <remarks>
    /// Storage comes from <c>AddThargaTeamRepository</c>, the unsubscribe route from <see cref="MapThargaMailUnsubscribe"/>, and
    /// every instance must share one persisted Data Protection key ring. A bounce handler is registered too, so mail sent
    /// from the address of a mailbox support reads is marked bounced when the receiving server returns it. Invitations
    /// are sent as mail to users, recorded and in the account category, unless the host registered its own
    /// <see cref="ITeamEmailSender"/>. Every mail is framed in the template set with
    /// <see cref="MailTemplateRegistration.AddThargaMailTemplate"/>, when there is one.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A category is not acceptable, an optional category is registered without an absolute <see cref="UserMailOptions.BaseUrl"/>, or a limit is out of range.</exception>
    public static IServiceCollection AddThargaUserMail(this IServiceCollection services, Action<UserMailOptions> configure = null)
    {
        var options = new UserMailOptions();
        configure?.Invoke(options);

        var categories = UserMailCategoryRegistry.Create(options.Categories, options.ServiceRequired);
        Validate(options, categories);

        services.TryAddSingleton(Options.Create(options));
        services.TryAddSingleton(categories);
        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();
        services.AddOutboundMailPolicy();
        services.AddMailTemplate();

        services.TryAddSingleton<UserMailUnsubscribeToken>();
        services.TryAddScoped(sp => new UserMailText(sp.GetService<ITextResolver>()));
        services.TryAddScoped<UserMailFooter>();
        services.TryAddScoped(sp => new UserMailPreferences(
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<IUserMailPreferenceStore>(),
            sp.GetService<CompositeAuditLogger>(),
            sp.GetService<IAuditEntryFactory>()));
        services.TryAddScoped<UserMailUnsubscribe>();
        services.TryAddSingleton<UserMailFrequencyCache>();
        services.TryAddScoped(sp => new UserMailFrequencies(
            sp.GetRequiredService<IUserMailOutbox>(),
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<UserMailFrequencyCache>(),
            sp.GetRequiredService<TimeProvider>()));

        services.TryAddScoped<IUserMailPreferenceService>(sp => new UserMailPreferenceService(
            sp.GetRequiredService<UserMailPreferences>(),
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<TeamAuthorizer>(),
            sp.GetRequiredService<IUserService>(),
            sp.GetRequiredService<IOptions<UserMailOptions>>().Value,
            sp.GetRequiredService<UserMailFrequencies>()));

        services.TryAddScoped<IUserMailQueue>(sp => new UserMailQueue(
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<IUserMailOutbox>(),
            sp.GetRequiredService<IOptions<UserMailOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<UserMailDispatcher>(),
            sp.GetService<ILogger<UserMailQueue>>()));

        services.TryAddScoped<IUserMailHistoryService>(sp => new UserMailHistoryService(
            sp.GetRequiredService<IUserMailQueue>(),
            sp.GetRequiredService<IUserMailRecipientDirectory>(),
            sp.GetRequiredService<TeamAuthorizer>(),
            sp.GetRequiredService<IUserService>(),
            sp.GetService<CompositeAuditLogger>(),
            sp.GetService<IAuditEntryFactory>()));

        services.TryAddScoped(sp => new UserMailDispatcher(
            sp.GetRequiredService<IUserMailOutbox>(),
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<UserMailPreferences>(),
            sp.GetRequiredService<IUserMailRecipientDirectory>(),
            new MailClient(Options.Create(options.Mail), sp.GetService<ILogger<MailClient>>()),
            sp.GetRequiredService<IOutboundMailPolicy>(),
            sp.GetRequiredService<UserMailFooter>(),
            sp.GetRequiredService<IOptions<UserMailOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<UserMailDispatcher>>(),
            sp.GetService<MailTemplate>(),
            sp.GetService<ContactMailSending>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, UserMailSender>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMailBounceHandler, UserMailBounceHandler>());
        RegisterInvitations(services);

        return services;
    }

    /// <summary>Sends invitations as mail to users, in place of the plain SMTP sender, unless the host registered a sender of its own.</summary>
    /// <remarks>Whichever order the two are registered in: the plain sender registered before is replaced, and one registered after finds this one and adds nothing.</remarks>
    private static void RegisterInvitations(IServiceCollection services)
    {
        var senders = services.Where(x => x.ServiceType == typeof(ITeamEmailSender)).ToArray();
        if (senders.Any(x => x.ImplementationType != typeof(InvitationMailSender))) return;

        foreach (var sender in senders) services.Remove(sender);

        services.AddScoped<ITeamEmailSender>(sp => new UserMailInvitationSender(
            new UserMailQueue(
                sp.GetRequiredService<UserMailCategoryRegistry>(),
                sp.GetRequiredService<IUserMailOutbox>(),
                sp.GetRequiredService<IOptions<UserMailOptions>>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<UserMailDispatcher>(),
                sp.GetService<ILogger<UserMailQueue>>()),
            sp.GetRequiredService<UserMailText>(),
            sp.GetRequiredService<IOptions<UserMailOptions>>()));
    }

    /// <summary>Maps the unsubscribe link's route at <see cref="UserMailOptions.UnsubscribePath"/>: GET shows a page that asks, POST unsubscribes, both without sign-in.</summary>
    /// <remarks>
    /// The token in the link authorizes the one change it names, so a host denying unauthenticated requests by default must
    /// allow this path, and POST takes no antiforgery token because a one-click request (RFC 8058) has none. A request
    /// signed in as the user the link is for — the host's authentication runs before the route, and
    /// <see cref="IUserService"/> resolves the same user key — also sees every category and its state; anyone else is
    /// offered <see cref="UserMailOptions.SignInPath"/>.
    /// </remarks>
    public static IEndpointRouteBuilder MapThargaMailUnsubscribe(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var path = endpoints.ServiceProvider.GetService<IOptions<UserMailOptions>>()?.Value.UnsubscribePath
                   ?? throw new InvalidOperationException($"Call {nameof(AddThargaUserMail)} before mapping the unsubscribe route.");

        endpoints.MapGet(path, UserMailUnsubscribeEndpoint.GetAsync).AllowAnonymous();
        endpoints.MapPost(path, UserMailUnsubscribeEndpoint.PostAsync).AllowAnonymous().DisableAntiforgery();

        return endpoints;
    }

    private static void Validate(UserMailOptions options, UserMailCategoryRegistry categories)
    {
        options.Mail ??= new MailOptions();

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) && !IsWebAddress(options.BaseUrl))
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.BaseUrl)} '{options.BaseUrl}' is not an absolute http or https address.");

        if (categories.HasOptional && string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new InvalidOperationException($"An optional mail category is registered, so its mail must carry an unsubscribe link: set {nameof(UserMailOptions)}.{nameof(UserMailOptions.BaseUrl)} to the site's absolute address.");

        if (string.IsNullOrWhiteSpace(options.UnsubscribePath) || !options.UnsubscribePath.StartsWith('/'))
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.UnsubscribePath)} must start with '/'.");

        RequireSitePathOrWebAddress(options.TermsUrl, nameof(UserMailOptions.TermsUrl));
        RequireSitePathOrWebAddress(options.PreferencesPath, nameof(UserMailOptions.PreferencesPath));
        RequireSitePathOrWebAddress(options.SignInPath, nameof(UserMailOptions.SignInPath));

        if (options.PageCss?.Contains("</", StringComparison.Ordinal) == true)
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.PageCss)} may hold style rules alone; '</' would end the page's style element.");

        if (options.MaxAttachmentBytes < 0 || options.MaxAttachmentBytes > UserMailOptions.AttachmentBytesCeiling)
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.MaxAttachmentBytes)} must be between 0 and {UserMailOptions.AttachmentBytesCeiling}, so a queued mail fits in one stored document.");

        if (options.MaxAttempts < 1)
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.MaxAttempts)} must be at least 1.");

        if (options.Lease <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.Lease)} must be longer than zero.");
    }

    private static bool IsWebAddress(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <exception cref="InvalidOperationException">The value is set and is neither a path on the site nor an absolute http or https address.</exception>
    private static void RequireSitePathOrWebAddress(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.Contains('\\')) return;
        if (IsWebAddress(trimmed)) return;

        throw new InvalidOperationException($"{nameof(UserMailOptions)}.{name} '{value}' is neither a path on the site, starting with '/', nor an absolute http or https address.");
    }
}
