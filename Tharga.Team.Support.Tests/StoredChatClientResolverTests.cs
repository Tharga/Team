using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Each team is answered with what it chose: the platform's model, its own provider, or nothing.
/// </summary>
public class StoredChatClientResolverTests
{
    private const string TeamA = "team-a";
    private const string GoodKey = "nl-team-key-0123456789";
    private const string Model = "claude-qwen3-coder:30b";

    private readonly SupportAiConnectionServiceTests.InMemoryConnectionStore _store = new();
    private readonly SupportAiConnectionServiceTests.FakeFactory _factory = new();
    private readonly EphemeralDataProtectionProvider _keyRing = new();
    private readonly PlatformChatClient _platform = new();

    [Fact]
    public async Task ATeamThatNeverChose_IsAnsweredWithThePlatformsModel()
    {
        Assert.Same(_platform, await Resolver().GetAsync(TeamA));
    }

    [Fact]
    public async Task WithoutAPlatformModel_ATeamThatNeverChose_HasNone()
    {
        Assert.Null(await Resolver(withPlatform: false).GetAsync(TeamA));
    }

    [Fact]
    public async Task Off_GivesNoModel()
    {
        await Service().TurnOffAsync(TeamA);

        Assert.Null(await Resolver().GetAsync(TeamA));
    }

    [Fact]
    public async Task ChoosingThePlatformsModel_GivesIt()
    {
        await Service().UsePlatformModelAsync(TeamA);

        Assert.Same(_platform, await Resolver().GetAsync(TeamA));
    }

    [Fact]
    public async Task AProvider_IsReachedWithTheTeamsOwnKey_AddressAndModel()
    {
        await ConnectAsync();
        var seen = _factory.Seen.Count;

        var client = await Resolver().GetAsync(TeamA);

        Assert.NotNull(client);
        Assert.NotSame(_platform, client);
        var endpoint = _factory.Seen[seen];
        Assert.Equal(GoodKey, endpoint.ApiKey);
        Assert.Equal("https://neurolito.com", endpoint.BaseAddress);
        Assert.Equal(Model, endpoint.Model);
    }

    /// <summary>A self-hosted server that asks for no key is reached without one.</summary>
    [Fact]
    public async Task AKindThatNeedsNoKey_IsReachedWithoutOne()
    {
        var factory = SupportAiConnectionServiceTests.SelfHosted();
        var connected = await Service(factory).ConnectAsync(TeamA, new SupportAiProviderRequest { Kind = SupportAiConnectionKind.Ollama, BaseAddress = "https://ollama.example/v1", Model = Model });
        Assert.True(connected.Succeeded, connected.Problem);

        var client = await Resolver(factory: factory).GetAsync(TeamA);

        Assert.NotNull(client);
        Assert.Null(factory.Seen[^1].ApiKey);
        Assert.Equal("https://ollama.example/v1", factory.Seen[^1].BaseAddress);
    }

    [Fact]
    public async Task TheClient_IsReused_UntilTheConnectionChanges()
    {
        await ConnectAsync();
        var cache = new SupportAiClientCache();

        var first = await Resolver(cache: cache).GetAsync(TeamA);
        Assert.Same(first, await Resolver(cache: cache).GetAsync(TeamA));

        await Service().ConnectAsync(TeamA, new SupportAiProviderRequest { Kind = SupportAiConnectionKind.Neurolito, Model = SupportAiConnectionServiceTests.FakeFactory.OtherModel });

        var second = await Resolver(cache: cache).GetAsync(TeamA);
        Assert.NotSame(first, second);
        Assert.Equal(SupportAiConnectionServiceTests.FakeFactory.OtherModel, _factory.Seen[^1].Model);
    }

    [Fact]
    public async Task AProviderTheHostDidNotInstall_GivesNoModel()
    {
        await ConnectAsync();

        Assert.Null(await Resolver(withFactory: false).GetAsync(TeamA));
    }

    [Fact]
    public async Task AKeyThatCanNoLongerBeDecrypted_GivesNoModel()
    {
        await ConnectAsync();

        var resolver = new StoredChatClientResolver(_store, new EphemeralDataProtectionProvider(), new SupportAiClientCache(), NullLogger<StoredChatClientResolver>.Instance, _platform, _factory);

        Assert.Null(await resolver.GetAsync(TeamA));
    }

    /// <summary>The whole path a member meets: a team that turned the assistant off is not offered it.</summary>
    [Fact]
    public async Task Readiness_FollowsTheTeamsChoice()
    {
        var responder = new ChatSupportResponder(Resolver(), new NoTools(), new SupportAssistantOptions(), NullLogger<ChatSupportResponder>.Instance);
        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(TeamA));

        await Service().TurnOffAsync(TeamA);

        Assert.Equal(SupportAssistantReadiness.NotConfigured, await responder.GetReadinessAsync(TeamA));
    }

    private StoredChatClientResolver Resolver(bool withPlatform = true, bool withFactory = true, SupportAiClientCache cache = null, ISupportChatClientFactory factory = null)
        => new(_store, _keyRing, cache ?? new SupportAiClientCache(), NullLogger<StoredChatClientResolver>.Instance, withPlatform ? _platform : null, withFactory ? factory ?? _factory : null);

    private SupportAiConnectionService Service(ISupportChatClientFactory factory = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(TeamClaimTypes.TeamKey, TeamA)], "test"));
        return new SupportAiConnectionService(_store, _keyRing, new TeamAuthorizer(new FixedPrincipalAccessor(principal)), TimeProvider.System, true, factory ?? _factory);
    }

    private async Task ConnectAsync()
    {
        var result = await Service().ConnectAsync(TeamA, new SupportAiProviderRequest { Kind = SupportAiConnectionKind.Neurolito, ApiKey = GoodKey, Model = Model });
        Assert.True(result.Succeeded, result.Problem);
    }

    private sealed class PlatformChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "From the platform.")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class NoTools : ISupportAssistantTools
    {
        public Task<IReadOnlyList<AITool>> ForCurrentCallerAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AITool>>([]);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
