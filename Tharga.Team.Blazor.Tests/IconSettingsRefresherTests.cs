using Microsoft.Extensions.DependencyInjection;
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

    private static IconSettingsRefresher Sut(IIconSettingsStore store, IconSettings live, TimeSpan? interval = null)
        => new(Provider(store), live, Options.Create(new ThargaBlazorOptions
        {
            // Zero means "load once, never re-read", which keeps the test from starting a timer it would
            // then have to wait on.
            IconSettingsRefreshInterval = interval ?? TimeSpan.Zero
        }));

    private sealed class FakeStore(IconSettingsState stored, Exception fails = null) : IIconSettingsStore
    {
        public Task<IconSettingsState> LoadAsync(CancellationToken cancellationToken = default)
            => fails != null ? Task.FromException<IconSettingsState>(fails) : Task.FromResult(stored);

        public Task SaveAsync(IconSettingsState settings, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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
}
