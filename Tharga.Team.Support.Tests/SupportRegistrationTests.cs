using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Notifications;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What <c>AddThargaSupport</c> actually puts in the container.
/// </summary>
/// <remarks>
/// Every class here builds a real provider with <c>ValidateOnBuild</c> and <c>ValidateScopes</c>. This
/// repo has already shipped a registration that compiled, unit-tested green, and then would not start —
/// a singleton that captured a scoped service. A registration test that only reads descriptors would
/// have missed it.
/// </remarks>
public class SupportRegistrationTests
{
    private static ServiceProvider Build(Action<IServiceCollection> configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void TheContainerValidates()
    {
        using var provider = Build(s => s.AddThargaSupport(o => o.Slack.BotToken = "xoxb-test"));

        Assert.NotNull(provider.GetRequiredService<SlackNotificationSink>());
        Assert.NotNull(provider.GetRequiredService<ISlackClient>());
        Assert.NotNull(provider.GetRequiredService<NotificationRouter>());
    }

    /// <summary>
    /// The self-check on the test above: it can only mean something if a captive dependency really does
    /// break the build. Without this, a future refactor that stopped validating would leave the guard
    /// passing while checking nothing.
    /// </summary>
    [Fact]
    public void TheContainerValidation_WouldCatchACaptiveDependency()
    {
        Assert.Throws<AggregateException>(() => Build(s =>
        {
            s.AddThargaSupport();
            s.AddScoped<CaptiveProbe>();
            s.AddSingleton<CaptiveHolder>();
        }));
    }

    private sealed class CaptiveProbe;

    private sealed class CaptiveHolder(CaptiveProbe probe)
    {
        public CaptiveProbe Probe { get; } = probe;
    }

    /// <summary>
    /// The audit fan-out finds the sink as an <see cref="IAuditLogger"/>; the host starts it as an
    /// <see cref="IHostedService"/>. <b>They must be the same object</b> — two instances would mean a
    /// queue nothing drains, which fails silently in exactly the way a notification feature cannot
    /// afford.
    /// </summary>
    [Fact]
    public void TheSinkAndTheHostedService_AreOneInstance()
    {
        using var provider = Build(s => s.AddThargaSupport());

        var sink = provider.GetRequiredService<SlackNotificationSink>();
        var asLogger = provider.GetServices<IAuditLogger>().OfType<SlackNotificationSink>().Single();
        var asHosted = provider.GetServices<IHostedService>().OfType<SlackNotificationSink>().Single();

        Assert.Same(sink, asLogger);
        Assert.Same(sink, asHosted);
    }

    /// <summary>
    /// Nothing is required. A host that registers the package and configures nothing gets a container
    /// that builds and a feature that stays quiet.
    /// </summary>
    [Fact]
    public void WithNoConfiguration_ItRegistersAndSendsNothing()
    {
        using var provider = Build(s => s.AddThargaSupport());

        var options = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;
        Assert.Null(options.DefaultChannel);
        Assert.NotEmpty(options.Routes);

        var router = provider.GetRequiredService<NotificationRouter>();
        Assert.Empty(router.Route(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.ServiceCall,
            Feature = "team",
            Action = "create"
        }));
    }

    [Fact]
    public void ConfigurationReachesTheRouterAndTheTransport()
    {
        using var provider = Build(s => s.AddThargaSupport(o =>
        {
            o.Slack.BotToken = "xoxb-configured";
            o.Notifications.DefaultChannel = "#configured";
            o.Notifications.Routes = [new NotificationRoute { Event = "team:create" }];
        }));

        Assert.Equal("xoxb-configured", provider.GetRequiredService<IOptions<SlackOptions>>().Value.BotToken);

        var message = Assert.Single(provider.GetRequiredService<NotificationRouter>().Route(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.ServiceCall,
            Feature = "team",
            Action = "create"
        }));
        Assert.Equal("#configured", message.Channel);
    }

    /// <summary>
    /// The whole chain in one test: an entry handed to the audit fan-out comes out as a Slack post.
    /// </summary>
    /// <remarks>
    /// Everything else here checks one link. This repeats what the composite does with
    /// <c>IEnumerable&lt;IAuditLogger&gt;</c>, so it fails if the sink is registered under a type the
    /// fan-out does not enumerate — which is the one wiring mistake that would leave every unit test
    /// green and every channel empty.
    /// </remarks>
    [Fact]
    public async Task AnAuditedEvent_ComesOutOfTheFanOutAsASlackPost()
    {
        var slack = Substitute.For<ISlackClient>();
        slack.PostAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SlackPostResult.Ok());

        using var provider = Build(s =>
        {
            s.AddSingleton(slack);
            s.AddThargaSupport(o => o.Notifications.DefaultChannel = "#team-events");
        });

        var composite = new CompositeAuditLogger(
            provider.GetServices<IAuditLogger>(),
            Options.Create(new AuditOptions()),
            null,
            provider.GetRequiredService<ILogger<CompositeAuditLogger>>());

        var sink = provider.GetRequiredService<SlackNotificationSink>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await sink.StartAsync(cts.Token);

        composite.Log(new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.ServiceCall,
            Feature = "team",
            Action = "create",
            CallerIdentity = "alice",
            CallerSource = AuditCallerSource.Web,
            Metadata = new Dictionary<string, string> { [AuditMetadataKeys.TeamName] = "Acme" }
        });

        while (slack.ReceivedCalls().Count() == 0 && !cts.IsCancellationRequested) await Task.Delay(10, CancellationToken.None);
        await sink.StopAsync(CancellationToken.None);

        await slack.Received(1).PostAsync("#team-events", Arg.Is<string>(t => t.Contains("Acme")), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A host that wants its own transport can replace it; the sink is written to an interface.</summary>
    [Fact]
    public void AHostCanSubstituteTheTransport()
    {
        var replacement = Substitute.For<ISlackClient>();

        using var provider = Build(s =>
        {
            s.AddSingleton(replacement);
            s.AddThargaSupport();
        });

        Assert.Same(replacement, provider.GetRequiredService<ISlackClient>());
    }
}

/// <summary>
/// Notifications ride the audit fan-out rather than a parallel mechanism, and that has a consequence
/// worth stating out loud rather than discovering in production.
/// </summary>
public class AuditSeamCouplingTests
{
    private static CompositeAuditLogger Composite(IAuditLogger sink, AuditOptions options)
        => new([sink], Options.Create(options), null, LoggerFactory.Create(_ => { }).CreateLogger<CompositeAuditLogger>());

    private static AuditEntry Entry(string action = "create") => new()
    {
        Timestamp = DateTime.UtcNow,
        EventType = AuditEventType.ServiceCall,
        Feature = "team",
        Action = action,
        CallerSource = AuditCallerSource.Web
    };

    [Fact]
    public void AnEntryTheAuditFilterKeeps_ReachesTheSink()
    {
        var sink = Substitute.For<IAuditLogger>();

        Composite(sink, new AuditOptions()).Log(Entry());

        sink.Received(1).Log(Arg.Any<AuditEntry>());
    }

    /// <summary>
    /// <b>The audit filter sits upstream of routing.</b> An <c>ExcludedActions</c> entry — or an
    /// <c>EventFilter</c> that drops the type — drops the notification with it, however the route
    /// reads. This is the documented cost of reusing the seam, asserted so it stays a known property.
    /// </summary>
    [Fact]
    public void AnEntryTheAuditFilterDrops_NeverReachesTheSink()
    {
        var sink = Substitute.For<IAuditLogger>();

        Composite(sink, new AuditOptions { ExcludedActions = ["create"] }).Log(Entry());

        sink.DidNotReceive().Log(Arg.Any<AuditEntry>());
    }

    [Fact]
    public void AnEventTypeTheAuditFilterExcludes_NeverReachesTheSink()
    {
        var sink = Substitute.For<IAuditLogger>();

        Composite(sink, new AuditOptions { EventFilter = AuditEventFilter.AuthEvents }).Log(Entry());

        sink.DidNotReceive().Log(Arg.Any<AuditEntry>());
    }
}
