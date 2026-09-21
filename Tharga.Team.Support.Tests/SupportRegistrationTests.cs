using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;
using Tharga.Team.Support.Email;
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
    /// <see cref="ISupportCaseService"/> can actually be resolved, not merely validated.
    /// </summary>
    /// <remarks>
    /// <b>This shipped unresolvable.</b> <c>AddThargaSupportCases</c> built the service in a factory that
    /// calls <c>GetRequiredService&lt;ISupportCaseNotifier&gt;()</c>, and nothing registered the notifier —
    /// so every host got <c>No service for type 'ISupportCaseNotifier'</c> the first time a page injected
    /// the case service, and the support feature could not be used at all.
    /// <para>
    /// <b>Container validation cannot catch this, which is the point of the test.</b> <c>ValidateOnBuild</c>
    /// inspects constructor parameters; a service registered through a factory lambda is opaque to it, and
    /// every dependency resolved inside that lambda is invisible until something asks for the service. The
    /// only guard that works is resolving it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCaseService_Resolves()
    {
        using var provider = Build(s =>
        {
            // What a host supplies through AddThargaTeam and AddThargaAuditLogging. Everything the support
            // package itself needs must come from AddThargaSupportCases.
            s.AddSingleton(Substitute.For<ISupportCaseStore>());
            s.AddSingleton(Substitute.For<ITeamPrincipalAccessor>());
            s.AddSingleton<TeamAuthorizer>();
            s.Configure<AuditOptions>(_ => { });
            s.AddSingleton<CompositeAuditLogger>();
            s.AddSingleton(Substitute.For<IAuditEntryFactory>());

            s.AddThargaSupportCases();
        });

        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISupportCaseService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISupportCaseNotifier>());
    }

    /// <summary>
    /// The whole mail path resolves in a real graph: two channels, the poller and the inbound handler.
    /// </summary>
    /// <remarks>
    /// <b>Resolved rather than validated, for the reason the notifier taught.</b> <c>ValidateOnBuild</c>
    /// cannot see inside a factory lambda, and <c>ISupportCaseService</c> is built by one — so a missing
    /// dependency of anything it reaches shows up only when something actually asks for it.
    /// </remarks>
    [Fact]
    public void TheMailPath_Resolves()
    {
        using var provider = Build(s =>
        {
            s.AddSingleton(Substitute.For<ISupportCaseStore>());
            s.AddSingleton(Substitute.For<ISupportEventLedger>());
            s.AddSingleton(Substitute.For<ITeamPrincipalAccessor>());
            s.AddSingleton<TeamAuthorizer>();
            s.Configure<AuditOptions>(_ => { });
            s.AddSingleton<CompositeAuditLogger>();
            s.AddSingleton(Substitute.For<IAuditEntryFactory>());

            // Both, as a host configuring a Slack channel for cases must: the transport and its client come
            // from AddThargaSupport, and the channel that uses them from AddThargaSupportCases.
            s.AddThargaSupport();
            s.AddThargaSupportCases(o =>
            {
                o.SlackChannel = "#support";
                o.Email.Imap.Host = "imap.example.com";
                o.Email.Smtp.Host = "smtp.example.com";
                o.Email.FromAddress = "support@fortdocs.se";
            });
        });

        using var scope = provider.CreateScope();

        var channels = scope.ServiceProvider.GetServices<ISupportChannel>().ToArray();

        Assert.Equal(2, channels.Length);
        Assert.Contains(channels, x => x.ChannelType == SupportChannelType.Slack);
        Assert.Contains(channels, x => x.ChannelType == SupportChannelType.Email);

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISupportCaseService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EmailEventHandler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISupportMailClient>());

        // The poller is a hosted service, so nothing else would ever construct it.
        Assert.Single(provider.GetServices<IHostedService>().OfType<SupportMailPoller>());
    }

    /// <summary>
    /// The notifier outlives every page that listens to it, and an inbound reply is raised from a different
    /// scope than the circuit waiting for it.
    /// </summary>
    [Fact]
    public void TheNotifier_IsOneInstanceForTheWholeApplication()
    {
        using var provider = Build(s =>
        {
            // What a host supplies through AddThargaTeam and AddThargaAuditLogging. Everything the support
            // package itself needs must come from AddThargaSupportCases.
            s.AddSingleton(Substitute.For<ISupportCaseStore>());
            s.AddSingleton(Substitute.For<ITeamPrincipalAccessor>());
            s.AddSingleton<TeamAuthorizer>();
            s.Configure<AuditOptions>(_ => { });
            s.AddSingleton<CompositeAuditLogger>();
            s.AddSingleton(Substitute.For<IAuditEntryFactory>());

            s.AddThargaSupportCases();
        });

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<ISupportCaseNotifier>(),
            second.ServiceProvider.GetRequiredService<ISupportCaseNotifier>());
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

/// <summary>
/// What the support module puts in the two scope catalogues, and the shape of the names it puts there.
/// </summary>
/// <remarks>
/// A scope nobody registered is grantable by nothing: it does not appear in the catalogue, the role editor
/// will not offer it, and <c>UnregisteredRoleScopeCheck</c> warns on a role that names it. So registering
/// the constant is a separate act from declaring it, and both need asserting.
/// </remarks>
public class SupportScopeCatalogueTests
{
    private static ISystemScopeRegistry SystemScopes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISupportCaseStore>());
        services.AddSingleton(Substitute.For<ITeamPrincipalAccessor>());
        services.AddSingleton<TeamAuthorizer>();
        services.Configure<AuditOptions>(_ => { });
        services.AddSingleton<CompositeAuditLogger>();
        services.AddSingleton(Substitute.For<IAuditEntryFactory>());
        services.AddThargaSupportCases();

        return services.BuildServiceProvider().GetRequiredService<ISystemScopeRegistry>();
    }

    [Theory]
    [InlineData(SystemSupportScopes.Read)]
    [InlineData(SystemSupportScopes.Manage)]
    [InlineData(SystemSupportScopes.AllRead)]
    [InlineData(SystemSupportScopes.AllManage)]
    public void EverySystemSupportScope_IsRegistered_WithADescription(string scope)
    {
        var definition = SystemScopes().All.SingleOrDefault(x => x.Name == scope);

        Assert.NotNull(definition);
        Assert.False(string.IsNullOrWhiteSpace(definition.Description));
    }

    /// <summary>
    /// The cross-team pair is <b>additional to</b> the unassigned pair, not a replacement for it. Reaching
    /// every team and reaching the cases no team owns are different grants, and a refactor that collapsed
    /// them would silently widen whoever holds one.
    /// </summary>
    [Fact]
    public void TheUnassignedPair_SurvivesTheCrossTeamPair()
    {
        var names = SystemScopes().All.Select(x => x.Name).ToList();

        Assert.Equal(4, names.Count(x => x.StartsWith("support:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The naming guard for the grammar documented on <see cref="ScopeDefinition"/>: feature, then reach,
    /// then action, with reach in the middle.
    /// </summary>
    /// <remarks>
    /// <b>This is what stops a rename hiding half of support from the audit log.</b> The feature is
    /// everything before the <i>first</i> colon, so <c>support:all:read</c> files under <c>support</c> with
    /// every other support entry, while a front-loaded <c>support-all:read</c> would file under a second
    /// feature — a second button on the audit filter bar, a second bar on its charts, and a key that no
    /// longer matches a <c>support:*</c> notification route.
    /// </remarks>
    [Theory]
    [InlineData(SupportScopes.Read, "support", "read")]
    [InlineData(SupportScopes.Manage, "support", "manage")]
    [InlineData(SystemSupportScopes.Read, "support", "unassigned:read")]
    [InlineData(SystemSupportScopes.Manage, "support", "unassigned:manage")]
    [InlineData(SystemSupportScopes.AllRead, "support", "all:read")]
    [InlineData(SystemSupportScopes.AllManage, "support", "all:manage")]
    public void EverySupportScope_AuditsUnderTheSupportFeature(string scope, string expectedFeature, string expectedAction)
    {
        var (feature, action) = AuditEntry.ParseScope(scope);

        Assert.Equal(expectedFeature, feature);
        Assert.Equal(expectedAction, action);
    }
}
