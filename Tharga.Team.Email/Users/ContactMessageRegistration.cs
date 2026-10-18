using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>Registers mail the product's staff write to contacts: people who are not users, whose addresses the host holds in its own records.</summary>
public static class ContactMessageRegistration
{
    private const string SendScopeDescription =
        "Write to contacts the host lists -- people who are not users -- see which of them opted out, read the log of every message sent to contacts, and opt an address out.";

    /// <summary>Registers <see cref="IContactMessageService"/>, gated by the system scope <see cref="SystemContactScopes.Send"/>, with the categories and the sender in <paramref name="configure"/>: contact mail is sent through the queue of mail to users, opted out of per address, and answered by the same unsubscribe route.</summary>
    /// <remarks>
    /// Call it after <see cref="UserMailRegistration.AddThargaUserMail"/>, whose queue, SMTP server, site address and
    /// unsubscribe route it uses; <see cref="UserMailOptions.BaseUrl"/> is required, because every contact mail carries an
    /// unsubscribe link. The opt-outs and the log come from <c>AddThargaTeamRepository</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Mail to users is not registered, it has no site address, no category is registered or one is not acceptable, a sender address is not one, or the list limit is out of range.</exception>
    public static IServiceCollection AddThargaContactMessages(this IServiceCollection services, Action<ContactMessageOptions> configure = null)
    {
        var userCategories = services.LastOrDefault(x => x.ServiceType == typeof(UserMailCategoryRegistry))?.ImplementationInstance as UserMailCategoryRegistry;
        var mailOptions = (services.LastOrDefault(x => x.ServiceType == typeof(IOptions<UserMailOptions>))?.ImplementationInstance as IOptions<UserMailOptions>)?.Value;
        if (userCategories == null || mailOptions == null)
            throw new InvalidOperationException($"Call {nameof(UserMailRegistration.AddThargaUserMail)} before {nameof(AddThargaContactMessages)}: contact mail is sent through the queue of mail to users.");

        var options = new ContactMessageOptions();
        configure?.Invoke(options);

        var categories = ContactMailCategoryRegistry.Create(options.Categories, userCategories);
        Validate(options, mailOptions);

        services.TryAddSingleton(Options.Create(options));
        services.TryAddSingleton(categories);
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped(sp => new ContactMailFooter(
            sp.GetRequiredService<UserMailUnsubscribeToken>(),
            sp.GetRequiredService<IOptions<UserMailOptions>>(),
            sp.GetRequiredService<IOptions<ContactMessageOptions>>(),
            sp.GetRequiredService<UserMailText>()));

        services.TryAddScoped(sp => new ContactMailSending(
            sp.GetRequiredService<ContactMailCategoryRegistry>(),
            sp.GetRequiredService<IContactMailSuppressionStore>(),
            sp.GetRequiredService<ContactMailFooter>(),
            new MailClient(Options.Create(ContactMailSender.MailOptions(sp.GetRequiredService<IOptions<UserMailOptions>>().Value, sp.GetRequiredService<IOptions<ContactMessageOptions>>().Value)), sp.GetService<ILogger<MailClient>>()),
            sp.GetRequiredService<IOptions<UserMailOptions>>(),
            sp.GetRequiredService<IOptions<ContactMessageOptions>>()));

        services.TryAddScoped(sp => new ContactMailUnsubscribe(
            sp.GetRequiredService<UserMailUnsubscribeToken>(),
            sp.GetRequiredService<ContactMailCategoryRegistry>(),
            sp.GetRequiredService<IContactMailSuppressionStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<CompositeAuditLogger>(),
            sp.GetService<IAuditEntryFactory>()));

        services.TryAddSingleton<ContactMailFrequencyCache>();
        services.TryAddScoped(sp => new ContactMailFrequencies(
            sp.GetRequiredService<IUserMailOutbox>(),
            sp.GetRequiredService<ContactMailCategoryRegistry>(),
            sp.GetRequiredService<ContactMailFrequencyCache>(),
            sp.GetRequiredService<TimeProvider>()));

        services.AddThargaSystemScopes(scopes => scopes.Register(SystemContactScopes.Send, SendScopeDescription));

        if (services.All(x => x.ServiceType != typeof(IContactMessageService)))
            services.AddSystemService<IContactMessageService, ContactMessageService>();

        return services;
    }

    private static void Validate(ContactMessageOptions options, UserMailOptions mailOptions)
    {
        if (string.IsNullOrWhiteSpace(mailOptions.BaseUrl))
            throw new InvalidOperationException($"Every contact mail carries an unsubscribe link: set {nameof(UserMailOptions)}.{nameof(UserMailOptions.BaseUrl)} to the site's absolute address.");

        RequireAddress(options.FromAddress, nameof(ContactMessageOptions.FromAddress));
        RequireAddress(options.ReplyTo, nameof(ContactMessageOptions.ReplyTo));

        if (options.MaxAddressees < 1 || options.MaxAddressees > ContactMessageOptions.AddresseesCeiling)
            throw new InvalidOperationException($"{nameof(ContactMessageOptions)}.{nameof(ContactMessageOptions.MaxAddressees)} must be between 1 and {ContactMessageOptions.AddresseesCeiling}, so the log of one message fits in one stored document.");
    }

    /// <exception cref="InvalidOperationException">The value is set and is not an address mail can be sent from or replied to.</exception>
    private static void RequireAddress(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        if (!MailboxAddress.TryParse(value.Trim(), out var mailbox) || !mailbox.Address.Contains('@'))
            throw new InvalidOperationException($"{nameof(ContactMessageOptions)}.{name} '{value}' is not a mail address.");
    }
}
