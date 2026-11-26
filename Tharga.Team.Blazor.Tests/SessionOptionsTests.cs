using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The settings and the registration of server-side sessions (Tharga/Team#471): the defaults the documentation states, the options reaching the Blazor layer
/// from the <c>AddThargaTeam</c> facade, and what <c>AddThargaTeamBlazor</c> registers, which is nothing active until a policy is.
/// </summary>
public class SessionOptionsTests
{
    [Fact]
    public void TheDefaults_AreThoseTheDocumentationStates()
    {
        var options = new SessionOptions();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CacheDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), options.ActivityInterval);
        Assert.Equal(TimeSpan.FromMinutes(1), options.IdleWarning);
        Assert.Null(options.EndedPath);
        Assert.True(options.AdoptExistingCookies);
        Assert.Equal(SessionErrorBehavior.Allow, options.OnError);
        Assert.Equal("Cookies", options.CookieScheme);
    }

    /// <summary>The warning is never longer than the idle timeout less two activity intervals: one to be heard, one to be recorded. Zero asks nobody, and nothing is left for a timeout too short.</summary>
    [Theory]
    [InlineData(600, 60, 60, 60)]
    [InlineData(600, 540, 60, 480)]
    [InlineData(600, 480, 60, 480)]
    [InlineData(600, 481, 60, 480)]
    [InlineData(180, 600, 60, 60)]
    [InlineData(120, 60, 60, 0)]
    [InlineData(60, 60, 60, 0)]
    [InlineData(600, 0, 60, 0)]
    [InlineData(600, -30, 60, 0)]
    public void TheIdleWarning_IsClampedToTheIdleTimeoutLessTwoActivityIntervals(int idleSeconds, int warningSeconds, int intervalSeconds, int expectedSeconds)
    {
        var options = new SessionOptions { IdleWarning = TimeSpan.FromSeconds(warningSeconds), ActivityInterval = TimeSpan.FromSeconds(intervalSeconds) };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.EffectiveIdleWarning(TimeSpan.FromSeconds(idleSeconds)));
    }

    [Fact]
    public void TheIdleWarning_IsForwardedFromTheFacade()
    {
        var from = new ThargaBlazorOptions();
        from.Sessions.IdleWarning = TimeSpan.FromSeconds(45);
        var to = new ThargaBlazorOptions();

        ThargaBlazorOptionsForwarder.Copy(from, to);

        Assert.Equal(TimeSpan.FromSeconds(45), to.Sessions.IdleWarning);
    }

    [Fact]
    public void TheBlazorOptions_CarryThem_NextToTeamEntry()
    {
        var options = new ThargaBlazorOptions();

        Assert.NotNull(options.Sessions);
        Assert.NotSame(options.TeamEntry, options.Sessions);
    }

    [Fact]
    public void TheFacadesForwarder_PicksThemUp()
    {
        Assert.Contains(nameof(ThargaBlazorOptions.Sessions), ThargaBlazorOptionsForwarder.ForwardableProperties().Select(p => p.Name));

        var from = new ThargaBlazorOptions();
        from.Sessions.CookieScheme = "HostCookies";
        var to = new ThargaBlazorOptions();
        ThargaBlazorOptionsForwarder.Copy(from, to);

        Assert.Equal("HostCookies", to.Sessions.CookieScheme);
    }

    private static ServiceCollection Host(Action<ServiceCollection> configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService, StubMember>();
            o.Sessions = Options();
        });
        configure?.Invoke(services);
        return services;
    }

    [Fact]
    public void WithNoPolicy_TheEvaluatorIsThere_AndNotActive()
    {
        using var scope = Host().BuildServiceProvider().CreateScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<SessionEvaluator>().Active);
    }

    [Fact]
    public void ARegisteredPolicy_MakesTheEvaluatorActive()
    {
        using var scope = Host(s => s.AddSingleton<ISessionPolicy>(new TestSessionPolicy())).BuildServiceProvider().CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<SessionEvaluator>().Active);
    }

    /// <summary>One cache per instance, shared by every request, as <c>TeamEntryCache</c> is: a cache per scope would cache nothing.</summary>
    [Fact]
    public void TheCache_IsOnePerInstance()
    {
        var provider = Host().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<SessionCache>(), provider.GetRequiredService<SessionCache>());
    }

    [Fact]
    public void TheSessionService_ResolvesOnceAStoreIsThere()
    {
        using var scope = Host(s => s.AddSingleton<IUserSessionStore>(new InMemorySessionStore())).BuildServiceProvider().CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserSessionService>());
    }

    [Fact]
    public void AHostsOwnSessionService_IsNotReplaced()
    {
        var own = new Moq.Mock<IUserSessionService>().Object;

        using var scope = Host(s => s.AddScoped(_ => own)).BuildServiceProvider().CreateScope();

        Assert.Same(own, scope.ServiceProvider.GetRequiredService<IUserSessionService>());
    }

    // ---- what is registered for the live watch

    /// <summary>The caches are the notifiers: one instance each, so an end announced anywhere reaches the cache that must forget it.</summary>
    [Fact]
    public void TheNotifiers_AreTheCachesThemselves_OnePerInstance()
    {
        var provider = Host().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<SessionCache>(), provider.GetRequiredService<IUserSessionNotifier>());
        Assert.Same(provider.GetRequiredService<TeamEntryCache>(), provider.GetRequiredService<ITeamEntryNotifier>());
    }

    private static Task StartNotifierCheckAsync(IServiceProvider provider)
        => provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<SessionNotifierWiringCheck>().Single().StartAsync(CancellationToken.None);

    /// <summary>
    /// A notifier of the host's own would disconnect the caches: nothing would forget, and the circuits would listen to a notifier the toolkit never raises.
    /// It is refused at startup, naming it, rather than replaced silently, whichever order it was registered in.
    /// </summary>
    [Fact]
    public async Task AHostsOwnSessionNotifier_IsRefusedAtStartup_NamingIt()
    {
        var own = new Moq.Mock<IUserSessionNotifier>().Object;
        var provider = Host(s => s.AddSingleton(own)).BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartNotifierCheckAsync(provider));

        Assert.Contains(nameof(IUserSessionNotifier), exception.Message);
    }

    [Fact]
    public async Task AHostsOwnSessionNotifier_RegisteredFirst_IsRefusedToo()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Moq.Mock<IUserSessionNotifier>().Object);
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService, StubMember>();
            o.Sessions = Options();
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartNotifierCheckAsync(services.BuildServiceProvider()));
    }

    [Fact]
    public async Task AHostsOwnTeamEntryNotifier_IsRefusedAtStartup_NamingIt()
    {
        var own = new Moq.Mock<ITeamEntryNotifier>().Object;
        var provider = Host(s => s.AddSingleton(own)).BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => StartNotifierCheckAsync(provider));

        Assert.Contains(nameof(ITeamEntryNotifier), exception.Message);
    }

    [Fact]
    public async Task TheToolkitsOwnNotifiers_PassTheCheck()
    {
        var provider = Host().BuildServiceProvider();

        Assert.Null(await Record.ExceptionAsync(() => StartNotifierCheckAsync(provider)));
    }

    [Fact]
    public void TheCircuit_IsOnePerCircuitScope_AndIsWhatTheViewIsGiven()
    {
        var provider = Host().BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(first.ServiceProvider.GetRequiredService<SessionCircuit>(), first.ServiceProvider.GetRequiredService<ISessionCircuit>());
        Assert.Same(first.ServiceProvider.GetRequiredService<ISessionCircuit>(), first.ServiceProvider.GetRequiredService<ISessionCircuit>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<ISessionCircuit>(), second.ServiceProvider.GetRequiredService<ISessionCircuit>());
    }

    [Fact]
    public void TheCircuitHandler_IsRegisteredOnce_ThoughTheHostRegistersTheLibraryTwice()
    {
        var services = Host();
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<StubTeamService, StubUserService, StubMember>());
        using var scope = services.BuildServiceProvider().CreateScope();

        var handlers = scope.ServiceProvider.GetServices<CircuitHandler>().OfType<SessionCircuitHandler>();

        Assert.Single(handlers);
    }

    [Fact]
    public void TheCircuit_ResolvesWithNoPolicy_AndWatchesNothing()
    {
        using var scope = Host().BuildServiceProvider().CreateScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<ISessionCircuit>().Active);
    }
}
