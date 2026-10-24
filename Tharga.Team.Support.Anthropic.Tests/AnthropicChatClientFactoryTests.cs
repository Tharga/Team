using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Anthropic.Tests;

/// <summary>
/// Reaching a provider over the Anthropic Messages API, against a fake server shaped like Neurolito: where requests go,
/// how the key is sent (as a bearer token, which Neurolito accepts), what a model list becomes, and how a failure is said.
/// </summary>
public class AnthropicChatClientFactoryTests
{
    private const string Key = "nl-team-key-0123456789";
    private const string Address = "https://neurolito.example";

    /// <summary>Neurolito's catalog: id, display name and type, without <c>created_at</c>.</summary>
    private const string NeurolitoModels = """
        {"data":[{"id":"claude-qwen3-coder:30b","display_name":"qwen3-coder:30b","type":"model"},{"id":"claude-llama3:70b","display_name":"llama3:70b","type":"model"}],"has_more":false,"first_id":"claude-qwen3-coder:30b","last_id":"claude-llama3:70b"}
        """;

    private const string Message = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-qwen3-coder:30b","content":[{"type":"text","text":"I can answer."}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":12,"output_tokens":4}}
        """;

    private readonly FakeServer _server = new();

    private static SupportAiEndpoint Endpoint(string model = "claude-qwen3-coder:30b") => new(SupportAiConnectionKind.Neurolito, Address, Key, model);

    [Fact]
    public void Neurolito_IsTheProviderOffered_AtItsAddress()
    {
        var provider = Assert.Single(Factory().Providers);

        Assert.Equal(SupportAiConnectionKind.Neurolito, provider.Kind);
        Assert.Equal("https://neurolito.com", provider.BaseAddress);
    }

    [Fact]
    public void TheStartingAddress_IsTheHostsToChange()
    {
        Assert.Equal("http://localhost:5224", Assert.Single(Factory(o => o.NeurolitoBaseAddress = "http://localhost:5224").Providers).BaseAddress);
    }

    [Fact]
    public async Task TheModels_AreListedFromTheAddress_WithTheKeyInItsHeader()
    {
        _server.Respond(HttpStatusCode.OK, NeurolitoModels);

        var list = await Factory().ListModelsAsync(Endpoint());

        Assert.Null(list.Problem);
        Assert.Equal(["claude-qwen3-coder:30b", "claude-llama3:70b"], list.Models.Select(x => x.Id));
        Assert.Equal("qwen3-coder:30b", list.Models[0].DisplayName);

        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{Address}/v1/models", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal($"Bearer {Key}", request.Headers["authorization"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AnthropicChatClientFactory.Refused)]
    [InlineData(HttpStatusCode.Forbidden, AnthropicChatClientFactory.NotPermitted)]
    [InlineData(HttpStatusCode.NotFound, AnthropicChatClientFactory.NoModelList)]
    [InlineData(HttpStatusCode.TooManyRequests, AnthropicChatClientFactory.Busy)]
    public async Task AFailure_IsSaidInWords_WithoutTheKey(HttpStatusCode status, string problem)
    {
        _server.Respond(status, "{\"type\":\"error\",\"error\":{\"type\":\"error\",\"message\":\"rejected " + Key + "\"}}");

        var list = await Factory().ListModelsAsync(Endpoint());

        Assert.Equal(problem, list.Problem);
        Assert.Empty(list.Models);
    }

    /// <summary>A wrong key will not become right on a second try, and somebody is waiting at the form.</summary>
    [Fact]
    public async Task AFailedListing_IsNotRetried()
    {
        _server.Respond(HttpStatusCode.InternalServerError, """{"type":"error","error":{"type":"api_error","message":"down"}}""");

        var list = await Factory().ListModelsAsync(Endpoint());

        Assert.Equal("The provider answered 500.", list.Problem);
        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task AnAddressThatCannotBeReached_IsSaidSo()
    {
        _server.Fail(new HttpRequestException("No such host is known."));

        Assert.Equal(AnthropicChatClientFactory.Unreachable, (await Factory().ListModelsAsync(Endpoint())).Problem);
    }

    [Fact]
    public async Task AKeyThatMayUseNoModel_IsSaidSo()
    {
        _server.Respond(HttpStatusCode.OK, """{"data":[],"has_more":false,"first_id":null,"last_id":null}""");

        Assert.Equal(AnthropicChatClientFactory.NoModels, (await Factory().ListModelsAsync(Endpoint())).Problem);
    }

    [Fact]
    public async Task AnAnswer_IsAskedOfTheChosenModel_AtTheAddress_WithTheKey()
    {
        _server.Respond(HttpStatusCode.OK, Message);

        using var chat = Factory().Create(Endpoint());
        var response = await chat.GetResponseAsync("Can you answer?");

        Assert.Equal("I can answer.", response.Text);
        var request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{Address}/v1/messages", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal($"Bearer {Key}", request.Headers["authorization"]);
        Assert.Contains("\"model\":\"claude-qwen3-coder:30b\"", request.Body);
    }

    [Fact]
    public void Registering_GivesTheFactory()
    {
        var services = new ServiceCollection();
        services.AddThargaSupportAnthropic();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<AnthropicChatClientFactory>(provider.GetRequiredService<ISupportChatClientFactory>());
    }

    private AnthropicChatClientFactory Factory(Action<AnthropicSupportOptions> configure = null)
    {
        var options = new AnthropicSupportOptions();
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddHttpClient(AnthropicChatClientFactory.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _server);

        return new AnthropicChatClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), options);
    }

    private sealed record SeenRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);

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
