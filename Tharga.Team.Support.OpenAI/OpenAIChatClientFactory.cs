using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.OpenAI;

/// <summary>
/// Reaches a provider over the OpenAI-compatible API: <c>GET {address}/models</c> to list what a key may use, and
/// <c>POST {address}/chat/completions</c> to answer.
/// </summary>
/// <remarks>
/// <b>OpenAI, Ollama and llama.cpp all speak this protocol</b>, so one client serves the three: only the address and
/// the key differ. The key is sent as a bearer token; <b>with no key, no <c>Authorization</c> header is sent</b>, so a
/// self-hosted server that asks for none is not handed a made-up one.
/// <para>
/// <b>HTTP connections come from <see cref="IHttpClientFactory"/></b>, so the clients a team's connection creates share
/// pooled connections rather than each opening their own.
/// </para>
/// <para>
/// A provider's failure while listing models is a problem for the person setting the connection up, so it comes back
/// as words, never as the exception, and never with the key.
/// </para>
/// </remarks>
internal sealed class OpenAIChatClientFactory(IHttpClientFactory httpClients, OpenAISupportOptions options) : ISupportChatClientFactory
{
    internal const string HttpClientName = "Tharga.Team.Support.OpenAI";
    internal static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);

    internal const string Refused = "The key was refused.";
    internal const string NotPermitted = "The key may not list models.";
    internal const string NoModelList = "There is no model list at this address. Check the address.";
    internal const string Busy = "The provider is busy. Try again shortly.";
    internal const string Unreachable = "The address could not be reached.";
    internal const string TooSlow = "The provider did not answer in time.";
    internal const string NotAProvider = "The address did not answer like an AI provider.";
    internal const string NoModels = "The provider offers no models for this key.";

    /// <summary>What the SDK is given when there is no key: it requires one, and <see cref="NoKeySent"/> takes it out again.</summary>
    private const string NoKey = "no-key";

    public IReadOnlyList<SupportAiProviderDefault> Providers =>
    [
        .. options.OfferOpenAI ? [new SupportAiProviderDefault(SupportAiConnectionKind.OpenAI, options.OpenAIBaseAddress)] : Array.Empty<SupportAiProviderDefault>(),
        .. options.OfferOllama ? [new SupportAiProviderDefault(SupportAiConnectionKind.Ollama, options.OllamaBaseAddress) { RequiresApiKey = false }] : Array.Empty<SupportAiProviderDefault>(),
        .. options.OfferLlamaCpp ? [new SupportAiProviderDefault(SupportAiConnectionKind.LlamaCpp, options.LlamaCppBaseAddress) { RequiresApiKey = false }] : Array.Empty<SupportAiProviderDefault>()
    ];

    public IChatClient Create(SupportAiEndpoint endpoint)
        => Client(endpoint, retries: null)
            .GetChatClient(endpoint.Model)
            .AsIChatClient()
            .AsBuilder()
            .ConfigureOptions(x => x.MaxOutputTokens ??= options.MaxOutputTokens)
            .UseFunctionInvocation()
            .Build();

    public async Task<SupportAiModelList> ListModelsAsync(SupportAiEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ListTimeout);

        try
        {
            // No retries: somebody is waiting at a form, and a wrong key or address is not going to become right.
            var result = await Client(endpoint, retries: 0).GetOpenAIModelClient().GetModelsAsync(deadline.Token);
            SupportAiModel[] models = [.. result.Value.Select(x => new SupportAiModel(x.Id, x.Id))];

            return models.Length == 0 ? Problem(NoModels) : new SupportAiModelList { Models = models };
        }
        catch (ClientResultException ex) when (ex.Status == 0)
        {
            return Problem(Unreachable);
        }
        catch (ClientResultException ex)
        {
            return Problem(ex.Status switch
            {
                401 => Refused,
                403 => NotPermitted,
                404 => NoModelList,
                429 => Busy,
                _ => $"The provider answered {ex.Status}."
            });
        }
        catch (JsonException)
        {
            return Problem(NotAProvider);
        }
        catch (HttpRequestException)
        {
            return Problem(Unreachable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(TooSlow);
        }
    }

    /// <param name="endpoint">Where, with which key.</param>
    /// <param name="retries">How many times to retry a failed request; null for the SDK's default.</param>
    private OpenAIClient Client(SupportAiEndpoint endpoint, int? retries)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint.BaseAddress),
            Transport = new HttpClientPipelineTransport(httpClients.CreateClient(HttpClientName))
        };
        if (retries != null) clientOptions.RetryPolicy = new ClientRetryPolicy(retries.Value);
        if (string.IsNullOrEmpty(endpoint.ApiKey)) clientOptions.AddPolicy(NoKeySent.Instance, PipelinePosition.BeforeTransport);

        return new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(endpoint.ApiKey) ? NoKey : endpoint.ApiKey), clientOptions);
    }

    private static SupportAiModelList Problem(string problem) => new() { Problem = problem };

    /// <summary>Takes out the header the SDK adds for its stand-in key, so a connection without a key sends none.</summary>
    private sealed class NoKeySent : PipelinePolicy
    {
        public static readonly NoKeySent Instance = new();

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Remove("Authorization");
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Remove("Authorization");
            await ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}
