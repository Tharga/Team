using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The startup half of the <see cref="SystemTeamScopes.Read"/> fix: a host whose team service derives
/// <see cref="TeamServiceBase"/> without cross-team listing is told so at boot, but only once something can
/// grant the scope. Granting it to such a host emptied the team page for every user in production, with nothing
/// in the log. Detection logic is covered by <c>TeamServiceCompletenessTests</c>; this covers reachability and
/// the wiring.
/// </summary>
/// <remarks>
/// <see cref="StubTeamService"/> overrides no cross-team member, so it is the reported host's shape.
/// <b>The silent cases are the ones that must not regress</b>: under <c>ThrowOnIncompleteTeamService</c> a false
/// positive stops a working host from booting.
/// </remarks>
public class TeamsReadCompletenessCheckTests
{
    private static ServiceCollection Host(Action<ServiceCollection> grant = null, bool throwOnIncomplete = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(
            sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService, StubMember>();
            o.ThrowOnIncompleteTeamService = throwOnIncomplete;
        });
        grant?.Invoke(services);
        return services;
    }

    private static Task StartCheckAsync(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var check = provider.GetServices<IHostedService>().Single(s => s.GetType().Name == "TeamServiceCompletenessCheck");
        return check.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NothingGrantsTeamsRead_Starts()
    {
        await StartCheckAsync(Host());
    }

    [Fact]
    public async Task ASystemRoleGrantsTeamsRead_FailsStartup_NamingTheMemberAndScope()
    {
        var services = Host(s => s.AddThargaSystemRoles(r => r.Map("Oversight", SystemTeamScopes.Read)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartCheckAsync(services));
        Assert.Contains(nameof(StubTeamService), ex.Message);
        Assert.Contains("GetAllTeamsInternalAsync", ex.Message);
        Assert.Contains(SystemTeamScopes.Read, ex.Message);
    }

    /// <summary>A system API key is issued from the scope registry, bypassing roles entirely.</summary>
    [Fact]
    public async Task TeamsReadIsARegisteredSystemScope_FailsStartup()
    {
        var services = Host(s => s.AddThargaSystemScopes(r => r.Register(SystemTeamScopes.Read, "See every team.")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartCheckAsync(services));
    }

    /// <summary>Some other system scope on a role is not a way to reach cross-team listing.</summary>
    [Fact]
    public async Task ARoleGrantsOnlyOtherSystemScopes_Starts()
    {
        await StartCheckAsync(Host(s => s.AddThargaSystemRoles(r => r.Map("Deleter", SystemTeamScopes.Delete))));
    }

    /// <summary>
    /// By default the gap is logged, not thrown: turning a pre-existing gap into a boot failure after a routine
    /// upgrade is the trade the facet half of the same check already declines.
    /// </summary>
    [Fact]
    public async Task ByDefault_TheGapDoesNotFailStartup()
    {
        var services = Host(s => s.AddThargaSystemRoles(r => r.Map("Oversight", SystemTeamScopes.Read)), throwOnIncomplete: false);

        await StartCheckAsync(services);
    }
}
