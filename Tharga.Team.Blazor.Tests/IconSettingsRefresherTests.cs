using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Startup load and periodic re-read of the site's icon settings.
/// </summary>
/// <remarks>
/// The interval itself is not asserted by waiting for it — a test that sleeps for a quarter of an hour is a
/// test nobody runs. What is asserted is the behaviour the interval exists to produce: stored values reach the
/// live singleton, a host with no store keeps what it was configured with, and a store that throws changes
/// nothing.
/// </remarks>
public class IconSettingsRefresherTests
{
    private static IServiceProvider Provider(IIconSettingsStore store)
    {
        var services = new ServiceCollection();
        if (store != null) services.AddScoped(_ => store);
        return services.BuildServiceProvider();
    }

    private static IconSettingsRefresher Sut(IIconSettingsStore store, IconSettings live, TimeSpan? interval = null, ILogger<IconSettingsRefresher> logger = null, IServiceProvider provider = null)
        => new(provider ?? Provider(store), live, Options.Create(new ThargaBlazorOptions
        {
            // Zero means "load once, never re-read", which keeps the test from starting a timer it would
            // then have to wait on.
            IconSettingsRefreshInterval = interval ?? TimeSpan.Zero
        }), logger);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class FakeStore(IconSettingsState stored, Exception fails = null) : IIconSettingsStore
    {
        public Task<IconSettingsState> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return fails != null ? Task.FromException<IconSettingsState>(fails) : Task.FromResult(stored);
        }

        public Task SaveAsync(IconSettingsState settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CapturingLogger : ILogger<IconSettingsRefresher>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) => Levels.Add(logLevel);
    }

    [Fact]
    public async Task Startup_WithStoredSettings_AppliesThemOverTheConfiguredValues()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.Identicon, AllowUserUpload = true };
        var store = new FakeStore(new IconSettingsState
        {
            GravatarStyle = GravatarStyles.RoboHash,
            AllowUserUpload = false
        });

        await Sut(store, live).StartAsync(CancellationToken.None);

        Assert.Equal(GravatarStyles.RoboHash, live.GravatarStyle);
        Assert.False(live.AllowUserUpload);
    }

    /// <summary>
    /// A host that has never saved anything keeps exactly what it configured at startup.
    /// </summary>
    /// <remarks>
    /// This is the promise that makes persistence safe to add to an existing host: nothing is stored until
    /// somebody changes a setting, so upgrading cannot silently re-point an application's avatars.
    /// </remarks>
    [Fact]
    public async Task Startup_WithNothingStored_LeavesTheConfiguredValuesAlone()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.Retro, GravatarEnabled = false };

        await Sut(new FakeStore(null), live).StartAsync(CancellationToken.None);

        Assert.Equal(GravatarStyles.Retro, live.GravatarStyle);
        Assert.False(live.GravatarEnabled);
    }

    [Fact]
    public async Task Startup_WithNoStoreRegistered_LeavesTheConfiguredValuesAlone()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.Wavatar };

        await Sut(null, live).StartAsync(CancellationToken.None);

        Assert.Equal(GravatarStyles.Wavatar, live.GravatarStyle);
    }

    /// <summary>A store that throws leaves the last known good values in place rather than resetting them.</summary>
    [Fact]
    public async Task Startup_WhenTheStoreThrows_KeepsTheCurrentValues()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.MonsterId };
        var store = new FakeStore(null, new InvalidOperationException("no connection"));

        await Sut(store, live).StartAsync(CancellationToken.None);

        Assert.Equal(GravatarStyles.MonsterId, live.GravatarStyle);
    }

    [Fact]
    public async Task Startup_WhenTheStoreThrows_SaysSoAsAWarning()
    {
        var logger = new CapturingLogger();

        await Sut(new FakeStore(null, new InvalidOperationException("no connection")), new IconSettings(), logger: logger).StartAsync(CancellationToken.None);

        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task AStartThatIsCancelled_IsNotReportedAsAStoreFailure()
    {
        var logger = new CapturingLogger();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sut(new FakeStore(null), new IconSettings(), logger: logger).StartAsync(cancelled.Token));

        Assert.Empty(logger.Levels);
    }

    /// <summary>
    /// A host whose start failed is disposed without <c>StopAsync</c>, which cancels the stopping token; the loop must
    /// end quietly then, or the host logs the cancellation as a fault at Error and Critical.
    /// </summary>
    [Fact]
    public async Task TheLoop_WhenTheStoppingTokenIsCancelled_EndsWithoutAFault()
    {
        using var sut = Sut(new FakeStore(null), new IconSettings(), TimeSpan.FromHours(1));
        await sut.StartAsync(CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var loop = sut.RunAsync(stopping.Token);

        await stopping.CancelAsync();

        await loop.WaitAsync(Patience);
        Assert.Equal(TaskStatus.RanToCompletion, loop.Status);
    }

    [Fact]
    public async Task TheLoop_WhenTheProviderIsDisposedUnderIt_EndsWithoutAFault()
    {
        var provider = (ServiceProvider)Provider(new FakeStore(null));
        using var sut = Sut(null, new IconSettings(), TimeSpan.FromMilliseconds(20), provider: provider);
        await sut.StartAsync(CancellationToken.None);
        await provider.DisposeAsync();

        var loop = sut.RunAsync(CancellationToken.None);

        await loop.WaitAsync(Patience);
        Assert.Equal(TaskStatus.RanToCompletion, loop.Status);
    }

    [Fact]
    public async Task TheLoop_ReReadsTheStore_UntilItIsStopped()
    {
        var live = new IconSettings { GravatarStyle = GravatarStyles.Retro };
        var store = new SwitchingStore();
        using var sut = Sut(store, live, TimeSpan.FromMilliseconds(20));
        await sut.StartAsync(CancellationToken.None);
        using var stopping = new CancellationTokenSource();
        var loop = sut.RunAsync(stopping.Token);

        store.Stored = new IconSettingsState { GravatarStyle = GravatarStyles.RoboHash };
        await Until(() => live.GravatarStyle == GravatarStyles.RoboHash);
        await stopping.CancelAsync();

        await loop.WaitAsync(Patience);
        Assert.Equal(GravatarStyles.RoboHash, live.GravatarStyle);
    }

    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Patience;
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(10);
    }

    private sealed class SwitchingStore : IIconSettingsStore
    {
        public volatile IconSettingsState Stored;

        public Task<IconSettingsState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);

        public Task SaveAsync(IconSettingsState settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
