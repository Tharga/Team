using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.OpenAI.Tests;

/// <summary>
/// Reaching a provider over the OpenAI-compatible API, against a fake server shaped like OpenAI, Ollama and llama.cpp:
/// which providers are offered and where, how the key is sent (as a bearer token, and not at all when there is none),
/// what a model list becomes, and how a failure is said.
/// </summary>
public class OpenAIChatClientFactoryTests
{
    private const string Key = "sk-team-key-0123456789";
    private const string Address = "https://api.openai.example/v1";
    private const string OllamaAddress = "https://ollama.example/v1";

    /// <summary>OpenAI's catalog: id, object, created and owner, no display name.</summary>
    private const string OpenAIModels = """
        {"object":"list","data":[{"id":"gpt-4.1","object":"model","created":1744316542,"owned_by":"system"},{"id":"gpt-4.1-mini","object":"model","created":1744318173,"owned_by":"system"}]}
        """;

    /// <summary>Ollama's catalog, as its OpenAI-compatible endpoint lists the models pulled to the server.</summary>
    private const string OllamaModels = """
        {"object":"list","data":[{"id":"llama3.2:latest","object":"model","created":1730000000,"owned_by":"library"}]}
        """;

    private const string Completion = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1730000000,"model":"gpt-4.1","choices":[{"index":0,"message":{"role":"assistant","content":"I can answer."},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":4,"total_tokens":16}}
        """;

    private readonly FakeServer _server = new();

    private static SupportAiEndpoint Endpoint(string model = "gpt-4.1") => new(SupportAiConnectionKind.OpenAI, Address, Key, model);

    /// <summary>OpenAI starts from its own address; a server of the team's own has no address to start from, and needs no key.</summary>
    [Fact]
    public void OpenAIOllamaAndLlamaCpp_AreOffered()
    {
        var providers = Factory().Providers;

        Assert.Equal([SupportAiConnectionKind.OpenAI, SupportAiConnectionKind.Ollama, SupportAiConnectionKind.LlamaCpp], providers.Select(x => x.Kind));
        Assert.Equal("https://api.openai.com/v1", providers[0].BaseAddress);
        Assert.True(providers[0].RequiresApiKey);
        Assert.All(providers.Skip(1), x => Assert.Null(x.BaseAddress));
        Assert.All(providers.Skip(1), x => Assert.False(x.RequiresApiKey));
    }

    [Fact]
    public void EachCanBeLeftOut_AndTheStartingAddresses_AreTheHostsToChange()
    {
        var providers = Factory(o =>
        {
            o.OfferOpenAI = false;
            o.OllamaBaseAddress = "https://ollama.internal.example/v1";
            o.OfferLlamaCpp = false;
        }).Providers;

        var provider = Assert.Single(providers);
        Assert.Equal(SupportAiConnectionKind.Ollama, provider.Kind);
        Assert.Equal("https://ollama.internal.example/v1", provider.BaseAddress);
    }

    [Fact]
    public async Task TheModels_AreListedFromTheAddress_WithTheKeyAsABearerToken()
    {
        _server.Respond(HttpStatusCode.OK, OpenAIModels);

        var list = await Factory().ListModelsAsync(Endpoint(model: null));

        Assert.Null(list.Problem);
        Assert.Equal(["gpt-4.1", "gpt-4.1-mini"], list.Models.Select(x => x.Id));
        Assert.Equal("gpt-4.1", list.Models[0].DisplayName);

        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{Address}/models", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal($"Bearer {Key}", request.Headers["authorization"]);
    }

    /// <summary>A server that asks for no key is sent none, rather than a made-up one.</summary>
    [Fact]
    public async Task WithoutAKey_NoKeyIsSent()
    {
        _server.Respond(HttpStatusCode.OK, OllamaModels);

        var list = await Factory().ListModelsAsync(new SupportAiEndpoint(SupportAiConnectionKind.Ollama, OllamaAddress, null, null));

        Assert.Equal("llama3.2:latest", Assert.Single(list.Models).Id);
        var request = Assert.Single(_server.Requests);
        Assert.Equal($"{OllamaAddress}/models", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.False(request.Headers.ContainsKey("authorization"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, OpenAIChatClientFactory.Refused)]
    [InlineData(HttpStatusCode.Forbidden, OpenAIChatClientFactory.NotPermitted)]
    [InlineData(HttpStatusCode.NotFound, OpenAIChatClientFactory.NoModelList)]
    [InlineData(HttpStatusCode.TooManyRequests, OpenAIChatClientFactory.Busy)]
    public async Task AFailure_IsSaidInWords_WithoutTheKey(HttpStatusCode status, string problem)
    {
        _server.Respond(status, "{\"error\":{\"message\":\"Incorrect API key provided: " + Key + "\",\"type\":\"invalid_request_error\"}}");

        var list = await Factory().ListModelsAsync(Endpoint());

        Assert.Equal(problem, list.Problem);
        Assert.Empty(list.Models);
    }

    /// <summary>A wrong key will not become right on a second try, and somebody is waiting at the form.</summary>
    [Fact]
    public async Task AFailedListing_IsNotRetried()
    {
        _server.Respond(HttpStatusCode.InternalServerError, """{"error":{"message":"down"}}""");

        var list = await Factory().ListModelsAsync(Endpoint());

        Assert.Equal("The provider answered 500.", list.Problem);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task AnAddressThatCannotBeReached_IsSaidSo()
    {
        _server.Fail(new HttpRequestException("No such host is known."));

        Assert.Equal(OpenAIChatClientFactory.Unreachable, (await Factory().ListModelsAsync(Endpoint())).Problem);
    }

    [Fact]
    public async Task AnAddressThatIsNotAProvider_IsSaidSo()
    {
        _server.Respond(HttpStatusCode.OK, "<html><body>Welcome</body></html>");

        Assert.Equal(OpenAIChatClientFactory.NotAProvider, (await Factory().ListModelsAsync(Endpoint())).Problem);
    }

    [Fact]
    public async Task AServerWithNoModels_IsSaidSo()
    {
        _server.Respond(HttpStatusCode.OK, """{"object":"list","data":[]}""");

        Assert.Equal(OpenAIChatClientFactory.NoModels, (await Factory().ListModelsAsync(Endpoint())).Problem);
    }

    [Fact]
    public async Task AnAnswer_IsAskedOfTheChosenModel_AtTheAddress_WithTheKey()
    {
        _server.Respond(HttpStatusCode.OK, Completion);

        using var chat = Factory(o => o.MaxOutputTokens = 1234).Create(Endpoint());
        var response = await chat.GetResponseAsync("Can you answer?");

        Assert.Equal("I can answer.", response.Text);
        Assert.Equal(16, response.Usage?.TotalTokenCount);
        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{Address}/chat/completions", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal($"Bearer {Key}", request.Headers["authorization"]);
        Assert.Contains("\"model\":\"gpt-4.1\"", request.Body);
        Assert.Contains("1234", request.Body);
    }

    [Fact]
    public void Registering_GivesTheFactory()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportOpenAIProviders(o => o.OfferOpenAI = false);

        using var provider = services.BuildServiceProvider();

        var factory = Assert.IsType<OpenAIChatClientFactory>(provider.GetRequiredService<ISupportChatClientFactory>());
        Assert.Equal(2, factory.Providers.Count);
    }

    /// <summary>Calling it again changes the options; it does not add a second factory offering the same kinds.</summary>
    [Fact]
    public void RegisteringTwice_KeepsOneFactory_WithTheLastOptions()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportOpenAIProviders();
        services.AddThargaSupportOpenAIProviders(o => o.OfferOpenAI = false);

        using var provider = services.BuildServiceProvider();

        var factory = Assert.IsType<OpenAIChatClientFactory>(Assert.Single(provider.GetServices<ISupportChatClientFactory>()));
        Assert.Equal(2, factory.Providers.Count);
    }

    /// <summary>Another provider package, or the host's own factory, stays beside this one rather than being replaced.</summary>
    [Fact]
    public void AnotherFactory_IsKeptBesideIt()
    {
        var own = new OwnFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ISupportChatClientFactory>(own);
        services.AddThargaSupportOpenAIProviders();

        using var provider = services.BuildServiceProvider();

        var factories = provider.GetServices<ISupportChatClientFactory>().ToArray();
        Assert.Equal(2, factories.Length);
        Assert.Same(own, factories[0]);
        Assert.IsType<OpenAIChatClientFactory>(factories[1]);
    }

    private OpenAIChatClientFactory Factory(Action<OpenAISupportOptions> configure = null)
    {
        var options = new OpenAISupportOptions();
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddHttpClient(OpenAIChatClientFactory.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _server);

        return new OpenAIChatClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), options);
    }

    private sealed record SeenRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);

    private sealed class OwnFactory : ISupportChatClientFactory
    {
        public IReadOnlyList<SupportAiProviderDefault> Providers => [];

        public IChatClient Create(SupportAiEndpoint endpoint) => throw new NotSupportedException();

        public Task<SupportAiModelList> ListModelsAsync(SupportAiEndpoint endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeServer : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "{}";
        private Exception _failure;

        public List<SeenRequest> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public void Fail(Exception failure) => _failure = failure;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(x => x.Key.ToLowerInvariant(), x => string.Join(",", x.Value));
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SeenRequest(request.Method, request.RequestUri, headers, body));

            if (_failure != null) throw _failure;

            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
