using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Service.Tests;

/// <summary>One policy for every sender, configured once, from <c>Email:Override</c>.</summary>
public class OutboundMailPolicyRegistrationTests
{
    private static ServiceProvider Build(Action<IServiceCollection> configure = null, Dictionary<string, string> settings = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Both modules call it; the second call must not register a second policy, binding or report.</summary>
    [Fact]
    public void RegisteringTwice_RegistersOnce()
    {
        var services = new ServiceCollection();

        services.AddOutboundMailPolicy();
        services.AddOutboundMailPolicy();

        Assert.Single(services, x => x.ServiceType == typeof(IOutboundMailPolicy));
        Assert.Single(services, x => x.ServiceType == typeof(IConfigureOptions<OutboundMailPolicyOptions>));
        Assert.Single(services, x => x.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void TheOptionsBindFromTheEmailOverrideSection()
    {
        using var provider = Build(s => s.AddOutboundMailPolicy(), new Dictionary<string, string>
        {
            ["Email:Override:Address"] = "test-inbox@eplicta.se",
            ["Email:Override:AllowedDomains:0"] = "eplicta.se",
            ["Email:Override:AllowedDomains:1"] = "fortdocs.se"
        });

        var options = provider.GetRequiredService<IOptions<OutboundMailPolicyOptions>>().Value;

        Assert.Equal("test-inbox@eplicta.se", options.Address);
        Assert.Equal(["eplicta.se", "fortdocs.se"], options.AllowedDomains);
    }

    [Fact]
    public void CodeConfigurationWorksWithoutAConfigurationSection()
    {
        using var provider = Build(s =>
        {
            s.AddOutboundMailPolicy();
            s.Configure<OutboundMailPolicyOptions>(o => o.Address = "test-inbox@eplicta.se");
        });

        Assert.Equal("test-inbox@eplicta.se", provider.GetRequiredService<IOptions<OutboundMailPolicyOptions>>().Value.Address);
    }

    [Fact]
    public void ThePolicyResolves_EvenWithNoConfigurationRegistered()
    {
        var services = new ServiceCollection();
        services.AddOutboundMailPolicy();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOutboundMailPolicy>());
    }

    [Theory]
    [InlineData("Production", null, null)]
    [InlineData("Staging", "test-inbox@eplicta.se", LogLevel.Information)]
    [InlineData("Staging", null, LogLevel.Warning)]
    public async Task AtStartup_ThePolicyInForceIsReported_OutsideProductionOnly(string environment, string address, LogLevel? expected)
    {
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);
        var policy = new OutboundMailPolicy(Options.Create(new OutboundMailPolicyOptions { Address = address }), host);
        var logger = new CapturingLogger<OutboundMailPolicyReport>();

        await new OutboundMailPolicyReport(policy, host, logger).StartAsync(CancellationToken.None);

        if (expected == null) Assert.Empty(logger.Entries);
        else Assert.Equal(expected, Assert.Single(logger.Entries).Level);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
