using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The provider recorded for an answer is where the call went, not what the client library calls itself.
/// </summary>
/// <remarks>
/// One client serves several kinds of connection (Neurolito and Anthropic speak the same protocol, OpenAI, Ollama and
/// llama.cpp share another), and a library names itself, so a Neurolito answer used to be recorded as Anthropic.
/// </remarks>
public class SupportChatClientProviderNameTests
{
    [Theory]
    [InlineData(SupportAiConnectionKind.Neurolito, "neurolito")]
    [InlineData(SupportAiConnectionKind.Anthropic, "anthropic")]
    [InlineData(SupportAiConnectionKind.OpenAI, "openai")]
    [InlineData(SupportAiConnectionKind.Ollama, "ollama")]
    [InlineData(SupportAiConnectionKind.LlamaCpp, "llamacpp")]
    public void ATeamsConnection_IsRecordedAsTheKindItWas_NotAsWhatTheLibraryCallsItself(SupportAiConnectionKind kind, string expected)
    {
        var factory = Composite(new LibraryNamedFactory(kind, "anthropic"));

        using var client = factory.Create(new SupportAiEndpoint(kind, "https://example.test", "key", "claude-haiku-5-5"));
        var metadata = client.GetService<ChatClientMetadata>();

        Assert.Equal(expected, metadata.ProviderName);
    }

    [Fact]
    public void TheModelAndTheAddress_AreStillWhatTheClientReported()
    {
        var factory = Composite(new LibraryNamedFactory(SupportAiConnectionKind.Neurolito, "anthropic"));

        using var client = factory.Create(new SupportAiEndpoint(SupportAiConnectionKind.Neurolito, "https://example.test", "key", "claude-haiku-5-5"));
        var metadata = client.GetService<ChatClientMetadata>();

        Assert.Equal("claude-haiku-5-5", metadata.DefaultModelId);
        Assert.Equal(new Uri("https://example.test"), metadata.ProviderUri);
    }

    [Fact]
    public void ClientsOtherThanMetadata_StillAnswerThroughTheWrappedClient()
    {
        var inner = new LibraryNamedClient("anthropic");
        var factory = Composite(new LibraryNamedFactory(SupportAiConnectionKind.Neurolito, "anthropic", inner));

        using var client = factory.Create(new SupportAiEndpoint(SupportAiConnectionKind.Neurolito, "https://example.test", "key", "m"));

        Assert.Same(inner, client.GetService<LibraryNamedClient>());
    }

    private static ISupportChatClientFactory Composite(ISupportChatClientFactory factory)
    {
        var services = new ServiceCollection().AddSingleton(factory).BuildServiceProvider();
        return CompositeSupportChatClientFactory.From(services);
    }

    private sealed class LibraryNamedFactory(SupportAiConnectionKind kind, string libraryName, LibraryNamedClient client = null) : ISupportChatClientFactory
    {
        public IReadOnlyList<SupportAiProviderDefault> Providers => [new(kind, "https://example.test")];

        public IChatClient Create(SupportAiEndpoint endpoint) => client ?? new LibraryNamedClient(libraryName, endpoint);

        public Task<SupportAiModelList> ListModelsAsync(SupportAiEndpoint endpoint, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class LibraryNamedClient(string name, SupportAiEndpoint endpoint = null) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null)
        {
            if (serviceType == typeof(ChatClientMetadata)) return new ChatClientMetadata(name, endpoint == null ? null : new Uri(endpoint.BaseAddress), endpoint?.Model);
            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose()
        {
        }
    }
}
