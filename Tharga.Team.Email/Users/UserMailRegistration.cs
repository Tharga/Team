using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Users;

/// <summary>Registers mail to users: categories, each user's choices, unsubscribe links, and a persisted queue sent in the background.</summary>
public static class UserMailRegistration
{
    /// <summary>Registers <see cref="IUserMailPreferenceService"/>, <see cref="IUserMailQueue"/> and the background sender, with the categories and settings in <paramref name="configure"/>.</summary>
    /// <remarks>Storage comes from <c>AddThargaTeamRepository</c>, the unsubscribe route from <see cref="MapThargaMailUnsubscribe"/>, and every instance must share one persisted Data Protection key ring.</remarks>
    /// <exception cref="InvalidOperationException">A category is not acceptable, an optional category is registered without an absolute <see cref="UserMailOptions.BaseUrl"/>, or a limit is out of range.</exception>
    public static IServiceCollection AddThargaUserMail(this IServiceCollection services, Action<UserMailOptions> configure = null)
    {
        var options = new UserMailOptions();
        configure?.Invoke(options);

        var categories = UserMailCategoryRegistry.Create(options.Categories);
        Validate(options, categories);

        services.TryAddSingleton(Options.Create(options));
        services.TryAddSingleton(categories);
        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();
        services.AddOutboundMailPolicy();

        services.TryAddSingleton<UserMailUnsubscribeToken>();
        services.TryAddScoped(sp => new UserMailText(sp.GetService<ITextResolver>()));
        services.TryAddScoped<UserMailFooter>();
        services.TryAddScoped(sp => new UserMailPreferences(
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<IUserMailPreferenceStore>(),
            sp.GetService<CompositeAuditLogger>(),
            sp.GetService<IAuditEntryFactory>()));
        services.TryAddScoped<UserMailUnsubscribe>();

        services.TryAddScoped<IUserMailPreferenceService>(sp => new UserMailPreferenceService(
            sp.GetRequiredService<UserMailPreferences>(),
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<TeamAuthorizer>(),
            sp.GetRequiredService<IUserService>()));

        services.TryAddScoped<IUserMailQueue>(sp => new UserMailQueue(
            sp.GetRequiredService<UserMailCategoryRegistry>(),
            sp.GetRequiredService<IUserMailOutbox>(),
            sp.GetRequiredService<IOptions<UserMailOptions>>(),
            sp.GetRequiredService<TimeProvider>()));

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
            sp.GetService<ILogger<UserMailDispatcher>>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, UserMailSender>());

        return services;
    }

    /// <summary>Maps the unsubscribe link's route at <see cref="UserMailOptions.UnsubscribePath"/>: GET shows a page that asks, POST unsubscribes, both without sign-in.</summary>
    /// <remarks>The token in the link authorizes the one change it names, so a host denying unauthenticated requests by default must allow this path, and POST takes no antiforgery token because a one-click request (RFC 8058) has none.</remarks>
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

        if (options.MaxAttachmentBytes < 0 || options.MaxAttachmentBytes > UserMailOptions.AttachmentBytesCeiling)
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.MaxAttachmentBytes)} must be between 0 and {UserMailOptions.AttachmentBytesCeiling}, so a queued mail fits in one stored document.");

        if (options.MaxAttempts < 1)
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.MaxAttempts)} must be at least 1.");

        if (options.Lease <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(UserMailOptions)}.{nameof(UserMailOptions.Lease)} must be longer than zero.");
    }

    private static bool IsWebAddress(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
