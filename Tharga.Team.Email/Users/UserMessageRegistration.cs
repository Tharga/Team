using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;

namespace Tharga.Team.Email.Users;

/// <summary>Registers messages the product's staff write to chosen users of its teams.</summary>
public static class UserMessageRegistration
{
    private const string SendScopeDescription =
        "Write a message to chosen users of any team -- specific users, a role, an access level or everyone, in chosen teams or all of them -- and read the log of every message sent.";

    /// <summary>Registers <see cref="IUserMessageService"/>, gated by the system scope <see cref="SystemMessageScopes.Send"/>, with the categories staff may write in.</summary>
    /// <remarks>Call it after <see cref="UserMailRegistration.AddThargaUserMail"/>; the team directory and the sent log come from <c>AddThargaTeamRepository</c>.</remarks>
    /// <exception cref="InvalidOperationException">Mail to users is not registered, no category is allowed, or one allowed is not registered.</exception>
    public static IServiceCollection AddThargaUserMessages(this IServiceCollection services, Action<UserMessageOptions> configure = null)
    {
        var categories = services.LastOrDefault(x => x.ServiceType == typeof(UserMailCategoryRegistry))?.ImplementationInstance as UserMailCategoryRegistry
                         ?? throw new InvalidOperationException($"Call {nameof(UserMailRegistration.AddThargaUserMail)} before {nameof(AddThargaUserMessages)}: messages are sent as mail to users, in its categories.");

        var options = new UserMessageOptions();
        configure?.Invoke(options);
        Validate(options, categories);

        services.TryAddSingleton(Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);

        services.AddThargaSystemScopes(scopes => scopes.Register(SystemMessageScopes.Send, SendScopeDescription));

        if (services.All(x => x.ServiceType != typeof(IUserMessageService)))
            services.AddSystemService<IUserMessageService, UserMessageService>();

        return services;
    }

    private static void Validate(UserMessageOptions options, UserMailCategoryRegistry categories)
    {
        var keys = options.StaffMessageCategories ?? [];

        if (keys.Length == 0)
            throw new InvalidOperationException($"{nameof(UserMessageOptions)}.{nameof(UserMessageOptions.StaffMessageCategories)} names no category, so staff could write in none.");

        var unknown = keys.Where(x => categories.Find(x) == null).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"{nameof(UserMessageOptions)}.{nameof(UserMessageOptions.StaffMessageCategories)} names {string.Join(", ", unknown.Select(x => $"'{x}'"))}, which is not a registered mail category. Register it with {nameof(UserMailOptions)}.{nameof(UserMailOptions.AddCategory)}.");
    }
}
