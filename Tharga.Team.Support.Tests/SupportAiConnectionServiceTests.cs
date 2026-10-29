using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Choosing what the assistant answers a team with: what may be chosen, that a provider must answer before it is saved,
/// and that the key goes in and never comes out.
/// </summary>
public class SupportAiConnectionServiceTests
{
    private const string TeamA = "team-a";
    private const string Admin = "admin-subject";
    internal const string GoodKey = "nl-team-key-0123456789";
    internal const string Model = "claude-qwen3-coder:30b";
    private const string NeurolitoAddress = "https://neurolito.com";
    private const string AnthropicAddress = "https://api.anthropic.com";

    private static SupportAiProviderRequest Neurolito(string apiKey = GoodKey, string model = Model, string baseAddress = null)
        => new() { Kind = SupportAiConnectionKind.Neurolito, ApiKey = apiKey, Model = model, BaseAddress = baseAddress };

    private const string OllamaAddress = "https://ollama.example/v1";

    /// <summary>A self-hosted kind: no key needed, and no address to start from, so the team gives one.</summary>
    internal static FakeFactory SelfHosted() => new()
    {
        AcceptsNoKey = true,
        Providers = [new(SupportAiConnectionKind.Ollama, null) { RequiresApiKey = false }]
    };

    private static SupportAiProviderRequest Ollama(string apiKey = null, string baseAddress = OllamaAddress, string model = Model)
        => new() { Kind = SupportAiConnectionKind.Ollama, ApiKey = apiKey, Model = model, BaseAddress = baseAddress };

    [Fact]
    public async Task AKindThatNeedsNoKey_ConnectsWithoutOne_AndStoresNone()
    {
        var (service, store, factory, _) = Build(factory: SelfHosted());

        var result = await service.ConnectAsync(TeamA, Ollama());

        Assert.True(result.Succeeded, result.Problem);
        Assert.Null(factory.Seen[^1].ApiKey);
        var stored = await store.GetAsync(TeamA);
        Assert.Equal(SupportAiConnectionKind.Ollama, stored.Kind);
        Assert.Equal(OllamaAddress, stored.BaseAddress);
        Assert.Null(stored.ProtectedApiKey);
        Assert.False((await service.GetConnectionAsync(TeamA)).HasApiKey);
    }

    /// <summary>A server that asks for a key gets the one the team gave, and keeps it while nothing else changes.</summary>
    [Fact]
    public async Task AKindThatNeedsNoKey_StillTakesOne()
    {
        var (service, store, factory, _) = Build(factory: SelfHosted());

        await service.ConnectAsync(TeamA, Ollama(apiKey: GoodKey));
        var result = await service.ConnectAsync(TeamA, Ollama(model: FakeFactory.OtherModel));

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(GoodKey, factory.Seen[^1].ApiKey);
        Assert.NotNull((await store.GetAsync(TeamA)).ProtectedApiKey);
    }

    [Fact]
    public async Task AKindWithNoAddressToStartFrom_AsksForOne()
    {
        var (service, _, factory, _) = Build(factory: SelfHosted());

        var result = await service.ListModelsAsync(TeamA, Ollama(baseAddress: " "));

        Assert.Equal("Enter the address.", result.Problem);
        Assert.Empty(factory.Seen);
    }

    /// <summary>Moving a keyless kind to a new address sends no key there, rather than the one stored for the old address.</summary>
    [Fact]
    public async Task AStoredKey_IsNotSentToANewAddress_EvenWhereNoneIsNeeded()
    {
        var (service, _, factory, _) = Build(factory: SelfHosted());
        await service.ConnectAsync(TeamA, Ollama(apiKey: GoodKey));

        await service.ListModelsAsync(TeamA, Ollama(baseAddress: "https://elsewhere.example/v1"));

        Assert.Equal("https://elsewhere.example/v1", factory.Seen[^1].BaseAddress);
        Assert.Null(factory.Seen[^1].ApiKey);
    }

    [Fact]
    public async Task TheChoices_AreThePlatformsModel_TheInstalledProviders_AndOff()
    {
        var (service, _, _, _) = Build();

        var choices = await service.GetChoicesAsync(TeamA);

        Assert.Equal([SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Off], choices.Kinds);
        Assert.Equal(NeurolitoAddress, Assert.Single(choices.ProviderDefaults).BaseAddress);
    }

    /// <summary>Every provider the installed factory offers is a choice, in its order, between the platform's model and off.</summary>
    [Fact]
    public async Task EveryProviderOffered_IsAChoice()
    {
        var (service, _, _, _) = Build(factory: new FakeFactory { Providers = BothProviders });

        var choices = await service.GetChoicesAsync(TeamA);

        Assert.Equal([SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Anthropic, SupportAiConnectionKind.Off], choices.Kinds);
        Assert.Equal([NeurolitoAddress, AnthropicAddress], choices.ProviderDefaults.Select(x => x.BaseAddress));
    }

    [Fact]
    public async Task ConnectingAnthropic_SavesIt_AtItsAddress()
    {
        var (service, store, _, _) = Build(factory: new FakeFactory { Providers = BothProviders });

        var result = await service.ConnectAsync(TeamA, new SupportAiProviderRequest { Kind = SupportAiConnectionKind.Anthropic, ApiKey = GoodKey, Model = Model });

        Assert.True(result.Succeeded, result.Problem);
        var stored = await store.GetAsync(TeamA);
        Assert.Equal(SupportAiConnectionKind.Anthropic, stored.Kind);
        Assert.Equal(AnthropicAddress, stored.BaseAddress);
    }

    /// <summary>The page says which model "the platform's model" is, as far as its client tells.</summary>
    [Fact]
    public async Task TheChoices_NameThePlatformsModel()
    {
        var (service, _, _, _) = Build(platformModelName: "ollama, llama3");

        Assert.Equal("ollama, llama3", (await service.GetChoicesAsync(TeamA)).PlatformModel);
    }

    [Theory]
    [InlineData("ollama", "llama3", "ollama, llama3")]
    [InlineData(null, "llama3", "llama3")]
    [InlineData("ollama", null, "ollama")]
    [InlineData(null, null, null)]
    public void ThePlatformsModel_IsDescribedFromWhatItsClientReports(string provider, string model, string expected)
    {
        var client = new DescribedChatClient(new ChatClientMetadata(provider, null, model));

        Assert.Equal(expected, SupportAiConnectionService.DescribePlatformModel(client));
    }

    /// <summary>A client that reports nothing about itself, or no client at all, is simply not named.</summary>
    [Fact]
    public void AClientThatSaysNothing_IsNotDescribed()
    {
        Assert.Null(SupportAiConnectionService.DescribePlatformModel(new DescribedChatClient(null)));
        Assert.Null(SupportAiConnectionService.DescribePlatformModel(null));
    }

    /// <summary>The usual registration wraps the client for function calls; the description comes through the wrapper.</summary>
    [Fact]
    public void AWrappedClient_IsDescribedByTheOneInside()
    {
        var client = new DescribedChatClient(new ChatClientMetadata("ollama", null, "llama3")).AsBuilder().UseFunctionInvocation().Build();

        Assert.Equal("ollama, llama3", SupportAiConnectionService.DescribePlatformModel(client));
    }

    [Fact]
    public async Task WithoutAPlatformModel_ItIsNotOffered()
    {
        var (service, _, _, _) = Build(platformModel: false);

        Assert.DoesNotContain(SupportAiConnectionKind.Platform, (await service.GetChoicesAsync(TeamA)).Kinds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UsePlatformModelAsync(TeamA));
    }

    [Fact]
    public async Task WithoutAProviderInstalled_OnlyThePlatformAndOffAreOffered()
    {
        var (service, _, _, _) = Build(withFactory: false);

        Assert.Equal([SupportAiConnectionKind.Platform, SupportAiConnectionKind.Off], (await service.GetChoicesAsync(TeamA)).Kinds);
        Assert.False((await service.ConnectAsync(TeamA, Neurolito())).Succeeded);
    }

    [Fact]
    public async Task ATeamThatNeverChose_IsOnThePlatformsModel()
    {
        var (service, _, _, _) = Build();

        var connection = await service.GetConnectionAsync(TeamA);

        Assert.Equal(SupportAiConnectionKind.Platform, connection.Kind);
        Assert.Null(connection.ChangedAt);
    }

    [Fact]
    public async Task Connecting_SavesTheProvider_WithTheKeyEncrypted_AndWhoDidIt()
    {
        var (service, store, _, _) = Build();

        var result = await service.ConnectAsync(TeamA, Neurolito());

        Assert.True(result.Succeeded, result.Problem);
        var stored = await store.GetAsync(TeamA);
        Assert.Equal(SupportAiConnectionKind.Neurolito, stored.Kind);
        Assert.Equal(NeurolitoAddress, stored.BaseAddress);
        Assert.Equal(Model, stored.Model);
        Assert.NotNull(stored.ProtectedApiKey);
        Assert.DoesNotContain(GoodKey, stored.ProtectedApiKey);
        Assert.Equal(Admin, stored.ChangedByIdentity);
    }

    /// <summary>The key goes in and is never read back: not from the read model, nor anything else the service returns.</summary>
    [Fact]
    public async Task TheConnection_AsRead_SaysAKeyIsSet_AndNeverShowsIt()
    {
        var (service, _, _, _) = Build();
        await service.ConnectAsync(TeamA, Neurolito());

        var connection = await service.GetConnectionAsync(TeamA);

        Assert.True(connection.HasApiKey);
        Assert.Equal(Model, connection.Model);
        Assert.DoesNotContain(GoodKey, connection.ToString());
    }

    [Fact]
    public async Task AKeyTheProviderRefuses_IsNotSaved()
    {
        var (service, store, _, _) = Build();

        var result = await service.ConnectAsync(TeamA, Neurolito(apiKey: "wrong-key"));

        Assert.False(result.Succeeded);
        Assert.Equal(FakeFactory.Refused, result.Problem);
        Assert.Null(await store.GetAsync(TeamA));
    }

    [Fact]
    public async Task AModelTheKeyMayNotUse_IsNotSaved()
    {
        var (service, store, _, _) = Build();

        var result = await service.ConnectAsync(TeamA, Neurolito(model: "not-offered"));

        Assert.False(result.Succeeded);
        Assert.Contains("not-offered", result.Problem);
        Assert.Null(await store.GetAsync(TeamA));
    }

    /// <summary>A provider that echoes the key in its error must not have it repeated to the screen.</summary>
    [Fact]
    public async Task AProblemThatEchoesTheKey_HasItTakenOut()
    {
        var (service, _, _, _) = Build(factory: new FakeFactory { EchoKeyInProblem = true });

        var models = await service.ListModelsAsync(TeamA, Neurolito(apiKey: "echoed-key-123"));

        Assert.DoesNotContain("echoed-key-123", models.Problem);
        Assert.Contains("(hidden)", models.Problem);
    }

    [Theory]
    [InlineData("http://neurolito.com")]
    [InlineData("ftp://neurolito.com")]
    [InlineData("not an address")]
    [InlineData("https://user:password@neurolito.com")]
    public async Task AnAddressThatIsNotHttps_OrCarriesCredentials_IsRefused(string address)
    {
        var (service, _, factory, _) = Build();

        var result = await service.ConnectAsync(TeamA, Neurolito(baseAddress: address));

        Assert.False(result.Succeeded);
        Assert.Empty(factory.Seen);
    }

    [Fact]
    public async Task HttpOnThisMachine_IsAllowed_ForALocalServer()
    {
        var (service, _, factory, _) = Build();

        var result = await service.ConnectAsync(TeamA, Neurolito(baseAddress: "http://localhost:5224/"));

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal("http://localhost:5224", factory.Seen[^1].BaseAddress);
    }

    [Fact]
    public async Task ChangingOnlyTheModel_KeepsTheStoredKey()
    {
        var (service, _, factory, _) = Build();
        await service.ConnectAsync(TeamA, Neurolito());

        var result = await service.ConnectAsync(TeamA, Neurolito(apiKey: null, model: FakeFactory.OtherModel));

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(GoodKey, factory.Seen[^1].ApiKey);
    }

    /// <summary>Otherwise anyone allowed to change the address could have the team's key sent to a server of their choosing.</summary>
    [Fact]
    public async Task AStoredKey_IsNeverSentToANewAddress()
    {
        var (service, _, factory, _) = Build();
        await service.ConnectAsync(TeamA, Neurolito());
        var seenBefore = factory.Seen.Count;

        var result = await service.ListModelsAsync(TeamA, Neurolito(apiKey: null, baseAddress: "https://elsewhere.example"));

        Assert.Equal("Enter the key.", result.Problem);
        Assert.Equal(seenBefore, factory.Seen.Count);
    }

    [Fact]
    public async Task AKeyThatCanNoLongerBeRead_AsksForItAgain()
    {
        var store = new InMemoryConnectionStore();
        var (service, _, _, _) = Build(store: store);
        await service.ConnectAsync(TeamA, Neurolito());

        // A different key ring: what protected the key is gone, as after a reset or on an instance that never shared it.
        var (other, _, _, _) = Build(store: store);

        Assert.Equal("The stored key can no longer be read. Enter it again.", (await other.ListModelsAsync(TeamA, Neurolito(apiKey: null))).Problem);
    }

    [Fact]
    public async Task UsingThePlatformsModel_OrTurningOff_ForgetsTheKey()
    {
        var (service, store, _, _) = Build();
        await service.ConnectAsync(TeamA, Neurolito());

        await service.UsePlatformModelAsync(TeamA);
        Assert.Null((await store.GetAsync(TeamA)).ProtectedApiKey);

        await service.ConnectAsync(TeamA, Neurolito());
        await service.TurnOffAsync(TeamA);
        var stored = await store.GetAsync(TeamA);
        Assert.Equal(SupportAiConnectionKind.Off, stored.Kind);
        Assert.Null(stored.ProtectedApiKey);
    }

    [Fact]
    public async Task ATest_ReturnsWhatTheModelSaid_AndSavesNothing()
    {
        var (service, store, _, _) = Build();

        var result = await service.TestAsync(TeamA, Neurolito());

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(FakeFactory.Reply, result.Reply);
        Assert.Null(await store.GetAsync(TeamA));
    }

    [Fact]
    public async Task AFailingTest_SaysWhy_WithoutTheKey()
    {
        var (service, _, _, _) = Build(factory: new FakeFactory { ChatFails = true });

        var result = await service.TestAsync(TeamA, Neurolito());

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(GoodKey, result.Problem);
    }

    [Fact]
    public async Task ACallerWithoutTheScope_IsRefusedEveryOperation()
    {
        var (service, _, _, _) = Build(withScope: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetChoicesAsync(TeamA));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetConnectionAsync(TeamA));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UsePlatformModelAsync(TeamA));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TurnOffAsync(TeamA));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConnectAsync(TeamA, Neurolito()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListModelsAsync(TeamA, Neurolito()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TestAsync(TeamA, Neurolito()));
    }

    [Fact]
    public async Task TheScope_OnlyCoversTheCallersOwnTeam()
    {
        var (service, _, _, _) = Build();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetConnectionAsync("team-b"));
    }

    [Fact]
    public async Task EveryChange_IsAudited_WithoutTheKey()
    {
        var (service, _, _, sink) = Build();

        await service.ConnectAsync(TeamA, Neurolito());
        await service.ConnectAsync(TeamA, Neurolito(apiKey: "wrong-key"));
        await service.TestAsync(TeamA, Neurolito());
        await service.UsePlatformModelAsync(TeamA);
        await service.TurnOffAsync(TeamA);

        Assert.Equal(["connect", "connect", "test", "use-platform", "turn-off"], sink.Entries.Select(x => x.Action));
        Assert.All(sink.Entries, x => Assert.Equal("assistant", x.Feature));
        Assert.Equal([true, false, true, true, true], sink.Entries.Select(x => x.Success));

        var connect = sink.Entries[0];
        Assert.Equal("Neurolito", connect.Metadata[SupportAuditMetadataKeys.AssistantConnection]);
        Assert.Equal(Model, connect.Metadata[SupportAuditMetadataKeys.AssistantModel]);

        foreach (var entry in sink.Entries)
        {
            Assert.DoesNotContain(entry.Metadata.Values, x => x != null && (x.Contains(GoodKey) || x.Contains("wrong-key")));
            Assert.False(entry.ErrorMessage?.Contains("wrong-key") ?? false);
        }
    }

    [Fact]
    public async Task ARefusal_IsAudited()
    {
        var (service, _, _, sink) = Build(withScope: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TurnOffAsync(TeamA));

        var entry = Assert.Single(sink.Entries);
        Assert.False(entry.Success);
        Assert.Equal("turn-off", entry.Action);
    }

    [Fact]
    public async Task PurgingATeam_RemovesItsConnection()
    {
        var store = new InMemoryConnectionStore();
        var (service, _, _, _) = Build(store: store);
        await service.ConnectAsync(TeamA, Neurolito());

        var purged = await new SupportAiConnectionPurgeParticipant(store).PurgeTeamDataAsync(TeamA);

        Assert.Equal(1, purged);
        Assert.Null(await store.GetAsync(TeamA));
        Assert.Equal(0, await new SupportAiConnectionPurgeParticipant(store).PurgeTeamDataAsync(TeamA));
    }

    private static readonly SupportAiProviderDefault[] BothProviders =
        [new(SupportAiConnectionKind.Neurolito, NeurolitoAddress), new(SupportAiConnectionKind.Anthropic, AnthropicAddress)];

    private static (ISupportAiConnectionService Service, InMemoryConnectionStore Store, FakeFactory Factory, CollectingAuditLogger Sink) Build(
        bool withScope = true,
        bool platformModel = true,
        bool withFactory = true,
        FakeFactory factory = null,
        InMemoryConnectionStore store = null,
        string platformModelName = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Admin),
            new(ClaimTypes.Name, "Ada Admin"),
            new(TeamClaimTypes.TeamKey, TeamA)
        };
        if (withScope) claims.Add(new Claim(TeamClaimTypes.Scope, SupportScopes.AssistantManage));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
        store ??= new InMemoryConnectionStore();
        factory ??= new FakeFactory();
        var sink = new CollectingAuditLogger();

        var service = new AuditingSupportAiConnectionServiceDecorator(
            new AuthorizationSupportAiConnectionServiceDecorator(
                new SupportAiConnectionService(store, new EphemeralDataProtectionProvider(), authorizer, TimeProvider.System, platformModel, withFactory ? factory : null, platformModelName),
                authorizer),
            new CompositeAuditLogger([sink], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));

        return (service, store, factory, sink);
    }

    internal sealed class InMemoryConnectionStore : ISupportAiConnectionStore
    {
        private readonly Dictionary<string, StoredSupportAiConnection> _byTeam = new();

        public Task<StoredSupportAiConnection> GetAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult(_byTeam.GetValueOrDefault(teamKey));

        public Task SetAsync(StoredSupportAiConnection connection, CancellationToken cancellationToken = default)
        {
            _byTeam[connection.TeamKey] = connection;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult(_byTeam.Remove(teamKey));
    }

    private sealed class DescribedChatClient(ChatClientMetadata metadata) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object GetService(Type serviceType, object serviceKey = null) => serviceType == typeof(ChatClientMetadata) ? metadata : null;
        public void Dispose() { }
    }

    internal sealed class FakeFactory : ISupportChatClientFactory
    {
        public const string Refused = "The key was refused.";
        public const string OtherModel = "claude-llama3:70b";
        public const string Reply = "I can answer.";

        public bool EchoKeyInProblem { get; init; }
        public bool ChatFails { get; init; }

        /// <summary>Answers without a key, as a self-hosted server that asks for none does.</summary>
        public bool AcceptsNoKey { get; init; }
        public List<SupportAiEndpoint> Seen { get; } = [];

        public IReadOnlyList<SupportAiProviderDefault> Providers { get; init; } = [new(SupportAiConnectionKind.Neurolito, NeurolitoAddress)];

        public IChatClient Create(SupportAiEndpoint endpoint)
        {
            Seen.Add(endpoint);
            return new StubChatClient(ChatFails ? null : Reply, endpoint.ApiKey);
        }

        public Task<SupportAiModelList> ListModelsAsync(SupportAiEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            Seen.Add(endpoint);

            if (EchoKeyInProblem) return Task.FromResult(new SupportAiModelList { Problem = $"Bad key: {endpoint.ApiKey}" });
            if (endpoint.ApiKey != GoodKey && !(AcceptsNoKey && endpoint.ApiKey == null)) return Task.FromResult(new SupportAiModelList { Problem = Refused });

            return Task.FromResult(new SupportAiModelList { Models = [new(Model, "Qwen3 Coder 30B"), new(OtherModel, "Llama 3 70B")] });
        }
    }

    private sealed class StubChatClient(string reply, string apiKey) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => reply == null
                ? throw new HttpRequestException($"401 Unauthorized for key {apiKey}")
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CollectingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
