using System.Security.Claims;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What <c>AddThargaSupportAiConnections</c> puts in the container. Everything is built in factories container
/// validation cannot see into, so each test resolves what it checks.
/// </summary>
public class SupportAiConnectionRegistrationTests
{
    [Fact]
    public void TheServiceAndTheResolver_Resolve()
    {
        using var provider = Build(after: s => s.AddThargaSupportAiConnections());
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISupportAiConnectionService>());
        Assert.IsType<StoredChatClientResolver>(scope.ServiceProvider.GetRequiredService<ISupportChatClientResolver>());
    }

    [Fact]
    public void CalledBeforeTheCases_TheStoredResolverStillWins()
    {
        using var provider = Build(before: s => s.AddThargaSupportAiConnections());
        using var scope = provider.CreateScope();

        Assert.IsType<StoredChatClientResolver>(scope.ServiceProvider.GetRequiredService<ISupportChatClientResolver>());
    }

    /// <summary>A team may bring its own provider on a site with no model of its own, so the assistant must exist to answer it.</summary>
    [Fact]
    public void WithoutAPlatformModel_ThereIsStillAnAssistant_ForTeamsThatBringTheirOwn()
    {
        using var provider = Build(after: s => s.AddThargaSupportAiConnections());
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<ISupportResponder>());
    }

    [Fact]
    public void TheScope_IsGrantedToAdministrators_ByDefault()
    {
        using var provider = Build(after: s => s.AddThargaSupportAiConnections());

        var registry = provider.GetRequiredService<IScopeRegistry>();

        Assert.Contains(SupportScopes.AssistantManage, registry.GetScopesForAccessLevel(AccessLevel.Administrator));
        Assert.DoesNotContain(SupportScopes.AssistantManage, registry.GetScopesForAccessLevel(AccessLevel.User));
    }

    [Fact]
    public void WithNoLevel_TheScopeIsRegistered_ButGrantedByNone()
    {
        using var provider = Build(after: s => s.AddThargaSupportAiConnections(o => o.ScopeAccessLevel = null));

        var registry = provider.GetRequiredService<IScopeRegistry>();

        Assert.Contains(registry.All, x => x.Name == SupportScopes.AssistantManage);
        Assert.DoesNotContain(SupportScopes.AssistantManage, registry.GetScopesForAccessLevel(AccessLevel.Owner));
    }

    [Fact]
    public void CalledTwice_RegistersOnce()
    {
        using var provider = Build(after: s =>
        {
            s.AddThargaSupportAiConnections();
            s.AddThargaSupportAiConnections();
        });

        Assert.Single(provider.GetServices<ITeamPurgeParticipant>().OfType<SupportAiConnectionPurgeParticipant>());
    }

    [Fact]
    public async Task TheFactory_TheHostRegisters_IsWhatTeamsMayChoose()
    {
        var administrator = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(TeamClaimTypes.TeamKey, "team-a"), new Claim(TeamClaimTypes.Scope, SupportScopes.AssistantManage)], "test"));
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(ValueTask.FromResult(administrator));

        using var provider = Build(after: s =>
        {
            s.AddThargaSupportAiConnections();
            s.AddSingleton<ISupportChatClientFactory>(new SupportAiConnectionServiceTests.FakeFactory());
            s.AddSingleton(Substitute.For<IChatClient>());
            s.AddSingleton(accessor);
        });
        using var scope = provider.CreateScope();

        var choices = await scope.ServiceProvider.GetRequiredService<ISupportAiConnectionService>().GetChoicesAsync("team-a");

        Assert.Equal([SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Off], choices.Kinds);
    }

    private static ServiceProvider Build(Action<IServiceCollection> before = null, Action<IServiceCollection> after = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        services.AddSingleton<ISupportAiConnectionStore>(new SupportAiConnectionServiceTests.InMemoryConnectionStore());
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
}
