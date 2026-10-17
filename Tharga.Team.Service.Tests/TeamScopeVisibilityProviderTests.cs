using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

public class TeamScopeVisibilityProviderTests
{
    [Fact]
    public async Task Default_Provider_Shows_Every_Scope()
    {
        var provider = new AllScopesVisibleTeamScopeVisibilityProvider();

        Assert.True(await provider.IsScopeVisibleAsync("team-1", "atlas:manage"));
        Assert.True(await provider.IsScopeVisibleAsync("team-2", "content:read"));
    }

    [Fact]
    public void AddThargaScopes_Registers_Default_Visibility_Provider()
    {
        var services = new ServiceCollection();
        services.AddThargaScopes(s => s.Register("doc:read", AccessLevel.Viewer));

        var provider = services.BuildServiceProvider().GetService<ITeamScopeVisibilityProvider>();

        Assert.IsType<AllScopesVisibleTeamScopeVisibilityProvider>(provider);
    }

    [Fact]
    public void Consumer_Registered_Provider_Overrides_Default()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITeamScopeVisibilityProvider, FeatureGatedScopeVisibilityProvider>();
        services.AddThargaScopes(s => s.Register("doc:read", AccessLevel.Viewer));

        var provider = services.BuildServiceProvider().GetService<ITeamScopeVisibilityProvider>();

        Assert.IsType<FeatureGatedScopeVisibilityProvider>(provider);
    }

    private sealed class FeatureGatedScopeVisibilityProvider : ITeamScopeVisibilityProvider
    {
        public Task<bool> IsScopeVisibleAsync(string teamKey, string scope, CancellationToken cancellationToken = default)
            => Task.FromResult(!scope.StartsWith("atlas:"));
    }
}
