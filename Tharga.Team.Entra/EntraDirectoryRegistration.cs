using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Entra;

public static class EntraDirectoryRegistration
{
    private const string AzureAdSectionName = "AzureAd";

    /// <summary>
    /// Registers Microsoft Entra ID as the platform's user directory (<see cref="IUserDirectoryService"/>),
    /// and as <see cref="IUserIdentityDirectory"/> unless the host registered its own.
    /// Binds <see cref="EntraDirectoryOptions"/> from the <c>AzureAd</c> configuration section
    /// (TenantId, ClientId, ClientSecret) — the section the platform sign-in already uses — then applies
    /// <paramref name="configure"/>. Requires app-only Graph permissions: <c>User.Read.All</c> for
    /// verification and directory listing, <c>User.ReadWrite.All</c> for directory deletion.
    /// </summary>
    public static IServiceCollection AddThargaEntraUserDirectory(this IServiceCollection services, IConfiguration configuration = null, Action<EntraDirectoryOptions> configure = null)
    {
        services.AddOptions<EntraDirectoryOptions>().Configure(options =>
        {
            configuration?.GetSection(AzureAdSectionName).Bind(options);
            configure?.Invoke(options);
        });

        services.TryAddSingleton<IEntraTokenProvider, CredentialEntraTokenProvider>();

        services.AddHttpClient<IUserDirectoryService, EntraUserDirectoryService>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<EntraDirectoryOptions>>().Value;
            client.BaseAddress = options.GraphBaseAddress;
        });

        services.TryAddTransient<IUserIdentityDirectory>(provider =>
            provider.GetRequiredService<IUserDirectoryService>() as IUserIdentityDirectory
            ?? throw new InvalidOperationException(
                $"The registered {nameof(IUserDirectoryService)} does not implement {nameof(IUserIdentityDirectory)}. Register an {nameof(IUserIdentityDirectory)} explicitly."));

        return services;
    }
}
