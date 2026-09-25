using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Tharga/Team#286: a host whose team service cannot look an invitation up by its code mints short links
/// that never resolve, and nothing said so. The startup check now names the member. Detection logic is
/// covered by <c>TeamServiceCompletenessTests</c>; this covers the wiring and the strict/lenient split.
/// </summary>
public class InviteLookupCompletenessCheckTests
{
    private static ServiceCollection Host<TTeamService>(bool throwOnIncomplete) where TTeamService : TeamServiceBase
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(
            sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<TTeamService, StubUserService, StubMember>();
            o.ThrowOnIncompleteTeamService = throwOnIncomplete;
        });
        return services;
    }

    private static Task StartCheckAsync(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        var check = provider.GetServices<IHostedService>().Single(s => s.GetType().Name == "TeamServiceCompletenessCheck");
        return check.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NoInviteLookup_Strict_FailsStartup_NamingTheServiceAndMember()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartCheckAsync(Host<StubTeamServiceWithoutInviteLookup>(throwOnIncomplete: true)));

        Assert.Contains(nameof(StubTeamServiceWithoutInviteLookup), ex.Message);
        Assert.Contains("GetTeamKeyByInviteKeyInternalAsync", ex.Message);
    }

    /// <summary>
    /// Logged, not thrown, by default — the gap has been there since short links shipped, and a host upgrading
    /// for something else should not find its application no longer boots.
    /// </summary>
    [Fact]
    public async Task NoInviteLookup_ByDefault_Starts()
    {
        await StartCheckAsync(Host<StubTeamServiceWithoutInviteLookup>(throwOnIncomplete: false));
    }

    [Fact]
    public async Task InviteLookupImplemented_Strict_Starts()
    {
        await StartCheckAsync(Host<StubTeamService>(throwOnIncomplete: true));
    }
}
