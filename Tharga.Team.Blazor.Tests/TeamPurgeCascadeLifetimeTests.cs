using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// <see cref="TeamPurgeCascade"/> must not outlive the stores its participants read.
/// </summary>
/// <remarks>
/// <b>This shipped broken.</b> The cascade was registered as a singleton while every participant that exists
/// — API keys, icons, support cases — reads a scoped store. That is not a subtle fault at purge time: the
/// application will not start, with <c>Cannot consume scoped service … from singleton 'TeamPurgeCascade'</c>.
/// It went unnoticed because container validation runs by default only in the Development environment, and
/// no test built a container holding both the cascade and a participant.
/// <para>
/// <b>The guard registers a participant and builds the real container.</b> Asserting the descriptor's
/// lifetime instead would be a test of the line rather than of the outcome — and would keep passing if the
/// cascade later captured something else with a longer life.
/// </para>
/// </remarks>
public class TeamPurgeCascadeLifetimeTests
{
    private const string ValidAzureAdConfig = """
        {
            "AzureAd": {
                "Authority": "https://test.ciamlogin.com/test",
                "ClientId": "test-client-id",
                "TenantId": "test-tenant-id",
                "CallbackPath": "/signin-oidc"
            }
        }
        """;

    /// <summary>Stands in for the shipped participants, every one of which reads a scoped store.</summary>
    private sealed class ScopedStoreParticipant(ScopedStore store) : ITeamPurgeParticipant
    {
        public string Name => "test";

        public Task<int> PurgeTeamDataAsync(string teamKey, CancellationToken cancellationToken = default)
            => Task.FromResult(store == null ? 0 : 1);
    }

    private sealed class ScopedStore;

    [Fact]
    public void TheRealContainer_ValidatesWithAParticipantThatReadsAScopedStore()
    {
        var builder = WebApplication.CreateBuilder();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig));
        builder.Configuration.AddJsonStream(stream);

        builder.AddThargaTeam();

        builder.Services.AddScoped<ScopedStore>();
        builder.Services.AddTransient<ITeamPurgeParticipant, ScopedStoreParticipant>();

        // Normally supplied by the Blazor and MongoDB runtimes.
        builder.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new TestNavigationManager());
        builder.Services.AddSingleton(new Moq.Mock<Microsoft.JSInterop.IJSRuntime>().Object);
        builder.Services.AddSingleton(new Moq.Mock<Tharga.MongoDB.IMongoDbServiceFactory>().Object);

        // Building is the assertion. ValidateOnBuild is what produces "Cannot consume scoped service ...
        // from singleton", which is the exact failure a host saw at startup, and it throws here rather than
        // returning anything to inspect.
        //
        // Deliberately not resolved afterwards: the real participants construct MongoDB repositories, so
        // resolving would test the mock factory rather than the lifetime.
        using var provider = builder.Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.NotNull(provider);

        Assert.NotEqual(
            ServiceLifetime.Singleton,
            builder.Services.Single(x => x.ServiceType == typeof(TeamPurgeCascade)).Lifetime);
    }

    private class TestNavigationManager : Microsoft.AspNetCore.Components.NavigationManager
    {
        public TestNavigationManager() => Initialize("https://localhost/", "https://localhost/");
    }
}
