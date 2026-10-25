using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Models;
using Microsoft.Extensions.AI;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Anthropic;

/// <summary>
/// Reaches a provider over the Anthropic Messages API: <c>GET /v1/models</c> to list what a key may use, and
/// <c>POST /v1/messages</c> to answer.
/// </summary>
/// <remarks>
/// <b>Neurolito and Anthropic both speak this protocol</b>, so one client serves both: only the address and the team's
/// key differ. The SDK sends the key as a bearer token, as Anthropic's API takes it and Neurolito accepts it beside its
/// own <c>X-API-KEY</c>.
/// <para>
/// <b>HTTP connections come from <see cref="IHttpClientFactory"/></b>, so the clients a team's connection creates share
/// pooled connections rather than each opening their own.
/// </para>
/// <para>
/// A provider's failure while listing models is a problem for the person setting the connection up, so it comes back
/// as words, never as the exception, and never with the key.
/// </para>
/// </remarks>
internal sealed class AnthropicChatClientFactory(IHttpClientFactory httpClients, AnthropicSupportOptions options) : ISupportChatClientFactory
{
    internal const string HttpClientName = "Tharga.Team.Support.Anthropic";
    internal static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);

    internal const string Refused = "The key was refused.";
    internal const string NotPermitted = "The key may not list models.";
    internal const string NoModelList = "There is no model list at this address. Check the address.";
    internal const string Busy = "The provider is busy. Try again shortly.";
    internal const string Unreachable = "The address could not be reached.";
    internal const string TooSlow = "The provider did not answer in time.";
    internal const string NotAProvider = "The address did not answer like an AI provider.";
    internal const string NoModels = "The provider offers no models for this key.";

    public IReadOnlyList<SupportAiProviderDefault> Providers =>
    [
        new(SupportAiConnectionKind.Neurolito, options.NeurolitoBaseAddress),
        .. options.OfferAnthropic ? [new SupportAiProviderDefault(SupportAiConnectionKind.Anthropic, options.AnthropicBaseAddress)] : Array.Empty<SupportAiProviderDefault>()
    ];

    public IChatClient Create(SupportAiEndpoint endpoint)
        => Client(endpoint, retries: null)
            .AsIChatClient(endpoint.Model, options.MaxOutputTokens)
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

    public async Task<SupportAiModelList> ListModelsAsync(SupportAiEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ListTimeout);

        try
        {
            // No retries: somebody is waiting at a form, and a wrong key or address is not going to become right.
            var page = await Client(endpoint, retries: 0).Models.List(new ModelListParams(), deadline.Token);
            var models = new List<SupportAiModel>();

            while (true)
            {
                models.AddRange(page.Items.Select(x => new SupportAiModel(x.ID, string.IsNullOrWhiteSpace(x.DisplayName) ? x.ID : x.DisplayName)));
                if (!page.HasNext()) break;
                page = await page.Next(deadline.Token);
            }

            return models.Count == 0 ? Problem(NoModels) : new SupportAiModelList { Models = [.. models] };
        }
        catch (AnthropicUnauthorizedException)
        {
            return Problem(Refused);
        }
        catch (AnthropicForbiddenException)
        {
            return Problem(NotPermitted);
        }
        catch (AnthropicNotFoundException)
        {
            return Problem(NoModelList);
        }
        catch (AnthropicRateLimitException)
        {
            return Problem(Busy);
        }
        catch (AnthropicApiException ex)
        {
            return Problem($"The provider answered {(int)ex.StatusCode}.");
        }
        catch (AnthropicInvalidDataException)
        {
            return Problem(NotAProvider);
        }
        catch (Exception ex) when (ex is AnthropicIOException or HttpRequestException)
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
    private AnthropicClient Client(SupportAiEndpoint endpoint, int? retries) => new()
    {
        ApiKey = endpoint.ApiKey,
        BaseUrl = endpoint.BaseAddress,
        HttpClient = httpClients.CreateClient(HttpClientName),
        MaxRetries = retries
    };

    private static SupportAiModelList Problem(string problem) => new() { Problem = problem };
}
