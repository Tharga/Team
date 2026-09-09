using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The throttle is actually applied to the registered invitation service.
/// </summary>
/// <remarks>
/// <b>Resolution, not registration.</b> A descriptor existing says nothing about what comes out of the
/// container — the same gap that let two startup defects reach a published release in 3.20.
/// <para>
/// The ordering hazard is specific here: the facets are registered with <c>TryAdd</c>, so a decorator added
/// before them would win and the inner service would never be built, and one added after without removing
/// the original would be shadowed by it. Neither mistake produces an error; both produce an unthrottled
/// service that looks wired.
/// </para>
/// </remarks>
public class InvitationThrottleRegistrationTests
{
    private sealed class StubInvitationService : ITeamInvitationService
    {
        public Task<TeamInvitation> GetInvitationAsync(string inviteCode) => Task.FromResult<TeamInvitation>(null);
    }

    [Fact]
    public void TheRegisteredService_IsThrottled()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITeamInvitationService, StubInvitationService>();

        services.AddInvitationThrottle();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ThrottledTeamInvitationService>(scope.ServiceProvider.GetRequiredService<ITeamInvitationService>());
    }

    /// <summary>The facets use TryAdd, so the decorator has to survive one being registered first.</summary>
    [Fact]
    public void ItSurvivesATryAddRegistrationOfTheSameInterface()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITeamInvitationService, StubInvitationService>();
        services.AddInvitationThrottle();

        services.TryAddScoped<ITeamInvitationService, StubInvitationService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ThrottledTeamInvitationService>(scope.ServiceProvider.GetRequiredService<ITeamInvitationService>());
    }

    /// <summary>A host with no invitation service registered must not gain a broken descriptor.</summary>
    [Fact]
    public void WithNothingToDecorate_ItDoesNothing()
    {
        var services = new ServiceCollection();

        services.AddInvitationThrottle();

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<ITeamInvitationService>());
    }

    [Fact]
    public void TheLifetimeIsPreserved()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITeamInvitationService, StubInvitationService>();

        services.AddInvitationThrottle();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ITeamInvitationService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>The decorated service still answers — the inner one is reached, not replaced.</summary>
    [Fact]
    public async Task TheInnerServiceIsStillCalled()
    {
        var inner = Substitute.For<ITeamInvitationService>();
        inner.GetInvitationAsync("code").Returns(new TeamInvitation("t-1", "Team One", "a@example.com", false));

        var services = new ServiceCollection();
        services.AddScoped(_ => inner);
        services.AddInvitationThrottle();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = await scope.ServiceProvider.GetRequiredService<ITeamInvitationService>().GetInvitationAsync("code");

        Assert.Equal("Team One", resolved.TeamName);
    }
}
