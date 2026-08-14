using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What enabling — and not enabling — access simulation puts in the container.
/// </summary>
public class AccessSimulationRegistrationTests
{
    /// <summary>
    /// The scope is registered at Administrator, which is what makes "team owner or administrator" the
    /// default audience without naming either: <c>GetScopesForAccessLevel</c> returns every registered
    /// scope at Administrator and above.
    /// </summary>
    [Fact]
    public void TheSimulationScopeIsGrantedToOwnerAndAdministratorAndNobodyElse()
    {
        var services = new ServiceCollection();
        services.AddThargaScopes(scopes =>
            scopes.Register(SimulationScopes.Simulate, AccessLevel.Administrator, "test"));

        var registry = services.BuildServiceProvider().GetRequiredService<IScopeRegistry>();

        Assert.Contains(SimulationScopes.Simulate, registry.GetScopesForAccessLevel(AccessLevel.Owner));
        Assert.Contains(SimulationScopes.Simulate, registry.GetScopesForAccessLevel(AccessLevel.Administrator));
        Assert.DoesNotContain(SimulationScopes.Simulate, registry.GetScopesForAccessLevel(AccessLevel.User));
        Assert.DoesNotContain(SimulationScopes.Simulate, registry.GetScopesForAccessLevel(AccessLevel.Viewer));
        Assert.DoesNotContain(SimulationScopes.Simulate, registry.GetScopesForAccessLevel(AccessLevel.Custom));
    }

    /// <summary>
    /// The self-check for the test above: it only means something if a scope registered at a *lower*
    /// level really would reach the other access levels. Otherwise "not granted to Viewer" could mean
    /// the registry grants nothing to anybody.
    /// </summary>
    [Fact]
    public void TheScopeRegistryReallyDoesGrantByLevel()
    {
        var services = new ServiceCollection();
        services.AddThargaScopes(scopes => scopes.Register("probe:read", AccessLevel.Viewer, "test"));

        var registry = services.BuildServiceProvider().GetRequiredService<IScopeRegistry>();

        Assert.Contains("probe:read", registry.GetScopesForAccessLevel(AccessLevel.Viewer));
        Assert.Contains("probe:read", registry.GetScopesForAccessLevel(AccessLevel.Owner));
    }

    [Fact]
    public void TheFeatureIsOffByDefault()
    {
        Assert.False(new AccessSimulationOptions().Enabled);
        Assert.False(new Tharga.Team.Blazor.Framework.ThargaBlazorOptions().Simulation.Enabled);
    }

    /// <summary>
    /// The enricher exists only when the feature is on. It reads a cookie on every audit entry, which a
    /// host that never enabled simulation should not pay for.
    /// </summary>
    [Fact]
    public void TheAuditEnricherIsRegisteredOnlyWhenEnabled()
    {
        Assert.Empty(EnrichersFor(enabled: false));
        Assert.Single(EnrichersFor(enabled: true));
    }

    private static IAuditEnricher[] EnrichersFor(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        if (enabled) services.AddSingleton<IAuditEnricher, AccessSimulationAuditEnricher>();

        return [.. services.BuildServiceProvider().GetServices<IAuditEnricher>().OfType<AccessSimulationAuditEnricher>()];
    }

    /// <summary>
    /// The container has to hand the enricher the accessor, or the circuit path is dead code: the
    /// parameter is optional so that a host registering the enricher alone still resolves, which is
    /// exactly what would let the fix silently not apply. Tharga/Team#220.
    /// </summary>
    [Fact]
    public void TheEnricherIsGivenTheCircuitPrincipalAccessor()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddSingleton<AccessSimulationPrincipalAccessor>();
        services.AddSingleton<IAuditEnricher, AccessSimulationAuditEnricher>();

        var provider = services.BuildServiceProvider();
        var enricher = provider.GetServices<IAuditEnricher>().OfType<AccessSimulationAuditEnricher>().Single();
        var principals = provider.GetRequiredService<AccessSimulationPrincipalAccessor>();

        var metadata = new Dictionary<string, string>();
        var simulation = new AccessSimulation { Kind = AccessSimulationKind.Scopes, Label = "Demo", Scopes = [] };

        using (principals.Push(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AccessSimulationCookie.ClaimType, AccessSimulationCookie.Write(simulation))], "Test"))))
        {
            enricher.Enrich(
                new AuditEntry { Timestamp = DateTime.UtcNow, EventType = AuditEventType.ServiceCall, Action = "probe" },
                metadata);
        }

        Assert.Equal("true", metadata[AccessSimulationMetadataKeys.Active]);
    }

    /// <summary>
    /// The self-check for the test above: without the accessor in the container the enricher still
    /// resolves — so the assertion there is about injection happening, not about the type existing.
    /// </summary>
    [Fact]
    public void TheEnricherStillResolvesWithoutTheAccessor()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuditEnricher, AccessSimulationAuditEnricher>();

        Assert.Single(services.BuildServiceProvider().GetServices<IAuditEnricher>().OfType<AccessSimulationAuditEnricher>());
    }
}
