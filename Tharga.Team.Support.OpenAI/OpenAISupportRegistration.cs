using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.OpenAI;

/// <summary>
/// Registration for reaching AI providers over the OpenAI-compatible API.
/// </summary>
public static class OpenAISupportRegistration
{
    /// <summary>
    /// Lets a team connect the support assistant to its own OpenAI key, or to an Ollama or llama.cpp server of its own,
    /// with the model chosen from what the provider offers.
    /// </summary>
    /// <remarks>
    /// Registers the <see cref="ISupportChatClientFactory"/> whose providers <c>AddThargaSupportAiConnections</c> offers to
    /// teams. Call both; this one alone gives teams nothing to choose with. Each kind can be left out through
    /// <see cref="OpenAISupportOptions"/>.
    /// <para>
    /// It sits beside <c>AddThargaSupportAiProviders</c> (Neurolito and Anthropic) and any factory of the host's own.
    /// Calling it again changes the options and adds no second factory.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddThargaSupportOpenAIProviders(this IServiceCollection services, Action<OpenAISupportOptions> configure = null)
    {
        var options = new OpenAISupportOptions();
        configure?.Invoke(options);

        services.AddHttpClient(OpenAIChatClientFactory.HttpClientName);
        services.RemoveAll<OpenAIChatClientFactory>();
        services.AddSingleton(sp => new OpenAIChatClientFactory(sp.GetRequiredService<IHttpClientFactory>(), options));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISupportChatClientFactory, OpenAIChatClientFactory>(sp => sp.GetRequiredService<OpenAIChatClientFactory>()));

        return services;
    }
}
