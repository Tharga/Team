using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// When <c>AddThargaSupportCases</c> registers an assistant, and where its models come from.
/// </summary>
/// <remarks>
/// The responder is built in a factory, which container validation cannot see into, so each test resolves it.
/// </remarks>
public class SupportAssistantRegistrationTests
{
    private const string TeamA = "team-a";
    private const string TeamB = "team-b";

    [Fact]
    public void WithNeitherAModelNorAResolver_ThereIsNoAssistant()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<ISupportResponder>());
    }

    [Fact]
    public async Task WithOneModelRegistered_EveryTeamIsAnsweredWithIt()
    {
        var registered = new StubChatClient();
        using var provider = Build(before: s => s.AddSingleton<IChatClient>(registered));
        using var scope = provider.CreateScope();

        var responder = scope.ServiceProvider.GetService<ISupportResponder>();
        Assert.NotNull(responder);
        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(TeamA));
        Assert.Same(registered, await scope.ServiceProvider.GetRequiredService<ISupportChatClientResolver>().GetAsync(TeamA));
    }

    [Fact]
    public async Task AHostResolverRegisteredBefore_GivesAnAssistantWithoutAModelOfItsOwn()
    {
        using var provider = Build(before: s => s.AddScoped<ISupportChatClientResolver, TeamAOnlyResolver>());

        await AssertTheHostResolverIsUsedAsync(provider);
    }

    [Fact]
    public async Task AHostResolverRegisteredAfter_GivesAnAssistantWithoutAModelOfItsOwn()
    {
        using var provider = Build(after: s => s.AddScoped<ISupportChatClientResolver, TeamAOnlyResolver>());

        await AssertTheHostResolverIsUsedAsync(provider);
    }

    private static async Task AssertTheHostResolverIsUsedAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();

        var responder = scope.ServiceProvider.GetService<ISupportResponder>();
        Assert.NotNull(responder);
        Assert.Equal(SupportAssistantReadiness.Ready, await responder.GetReadinessAsync(TeamA));
        Assert.Equal(SupportAssistantReadiness.NotConfigured, await responder.GetReadinessAsync(TeamB));
    }

    private static ServiceProvider Build(Action<IServiceCollection> before = null, Action<IServiceCollection> after = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        services.AddSingleton(Substitute.For<ITeamPrincipalAccessor>());
        services.AddSingleton<TeamAuthorizer>();
        services.Configure<AuditOptions>(_ => { });
        services.AddSingleton<CompositeAuditLogger>();
        services.AddSingleton(Substitute.For<IAuditEntryFactory>());

        before?.Invoke(services);
        services.AddThargaSupportCases();
        after?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private sealed class TeamAOnlyResolver : ISupportChatClientResolver
    {
        public Task<IChatClient> GetAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult<IChatClient>(teamKey == TeamA ? new StubChatClient() : null);
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "An answer.")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
