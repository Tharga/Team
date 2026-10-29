using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Every registered <see cref="ISupportChatClientFactory"/> as one, so a host can install more than one provider package.
/// </summary>
/// <remarks>
/// <b>Each kind is reached through the last registered factory that offers it</b>, as a later registration overrides an
/// earlier one everywhere else in DI. A kind is offered once, where the first factory offering it put it in the list,
/// with the address the factory that reaches it starts from.
/// </remarks>
internal sealed class CompositeSupportChatClientFactory : ISupportChatClientFactory
{
    private readonly ISupportChatClientFactory[] _factories;

    private CompositeSupportChatClientFactory(ISupportChatClientFactory[] factories) => _factories = factories;

    /// <summary>The registered factories as one, or null when there are none, so a team chooses between the platform's model and off.</summary>
    public static ISupportChatClientFactory From(IServiceProvider services)
    {
        ISupportChatClientFactory[] factories = [.. services.GetServices<ISupportChatClientFactory>()];
        return factories.Length == 0 ? null : new CompositeSupportChatClientFactory(factories);
    }

    public IReadOnlyList<SupportAiProviderDefault> Providers =>
    [
        .. _factories
            .SelectMany(x => x.Providers)
            .Select(x => x.Kind)
            .Distinct()
            .Select(kind => For(kind).Providers.Last(x => x.Kind == kind))
    ];

    public IChatClient Create(SupportAiEndpoint endpoint) => For(endpoint.Kind).Create(endpoint);

    public Task<SupportAiModelList> ListModelsAsync(SupportAiEndpoint endpoint, CancellationToken cancellationToken = default)
        => For(endpoint.Kind).ListModelsAsync(endpoint, cancellationToken);

    private ISupportChatClientFactory For(SupportAiConnectionKind kind)
        => _factories.LastOrDefault(x => x.Providers.Any(p => p.Kind == kind))
           ?? throw new InvalidOperationException($"No registered factory reaches {kind}.");
}
