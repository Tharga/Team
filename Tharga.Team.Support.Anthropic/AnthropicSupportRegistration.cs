using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Anthropic;

/// <summary>
/// Registration for reaching AI providers over the Anthropic Messages API.
/// </summary>
public static class AnthropicSupportRegistration
{
    /// <summary>
    /// Lets a team connect the support assistant to its own Neurolito or Anthropic key, with the model chosen from what
    /// the provider offers that key.
    /// </summary>
    /// <remarks>
    /// Registers the <see cref="ISupportChatClientFactory"/> whose providers <c>AddThargaSupportAiConnections</c> offers to
    /// teams. Call both; this one alone gives teams nothing to choose with. Set
    /// <see cref="AnthropicSupportOptions.OfferAnthropic"/> to <c>false</c> to offer Neurolito only.
    /// </remarks>
    public static IServiceCollection AddThargaSupportAiProviders(this IServiceCollection services, Action<AnthropicSupportOptions> configure = null)
    {
        var options = new AnthropicSupportOptions();
        configure?.Invoke(options);

        services.AddHttpClient(AnthropicChatClientFactory.HttpClientName);
        services.RemoveAll<ISupportChatClientFactory>();
        services.AddSingleton<ISupportChatClientFactory>(sp => new AnthropicChatClientFactory(sp.GetRequiredService<IHttpClientFactory>(), options));

        return services;
    }

    /// <summary>The name <see cref="AddThargaSupportAiProviders"/> had until 4.5.</summary>
    [Obsolete($"Renamed to {nameof(AddThargaSupportAiProviders)}.")]
    public static IServiceCollection AddThargaSupportAnthropic(this IServiceCollection services, Action<AnthropicSupportOptions> configure = null)
        => services.AddThargaSupportAiProviders(configure);
}
