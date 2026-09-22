using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Loads the site's stored <see cref="IconSettings"/> at startup and re-reads them periodically, so a change
/// made on one instance reaches the others.
/// </summary>
/// <remarks>
/// <b>The singleton is the cache; there is nothing else to clear.</b> Saving applies the new values to
/// <see cref="IconSettings"/> directly, so the instance that handled the change is correct immediately — which
/// on a single-instance host means everywhere. This timer exists only for the instances that did <i>not</i>
/// handle it, which is why the interval is quarter-hours rather than seconds: it is the slow path for a rare
/// change, not a polling loop anything waits on.
/// <para>
/// <b>The cost, stated rather than discovered:</b> an instance that did not handle the save renders the old
/// style for up to one interval. That is an acceptable trade for avatar presentation and would not be for
/// anything security-bearing — claims have their own, much shorter, revalidation for that reason.
/// </para>
/// <para>
/// Does nothing when no <see cref="IIconSettingsStore"/> is registered, which is the case for a host that
/// persists nothing: the values configured at startup then stay in force, untouched.
/// </para>
/// </remarks>
internal sealed class IconSettingsRefresher(
    IServiceProvider serviceProvider,
    IconSettings settings,
    IOptions<ThargaBlazorOptions> options,
    ILogger<IconSettingsRefresher> logger = null) : BackgroundService
{
    /// <remarks>
    /// <b>The first load happens here rather than in <see cref="ExecuteAsync"/>, and is awaited.</b>
    /// <c>BackgroundService.StartAsync</c> does not wait for <c>ExecuteAsync</c> — it keeps the task and
    /// returns — so loading there would let the first requests render avatars against whatever the host
    /// configured, then change under them a moment later. Here the host does not finish starting until the
    /// stored settings are in force.
    /// </remarks>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _hasStore = await ApplyStoredAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    private bool _hasStore;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.IconSettingsRefreshInterval;
        if (!_hasStore || interval <= TimeSpan.Zero) return;

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ApplyStoredAsync(stoppingToken);
    }

    /// <returns>Whether a store exists, and so whether there is any point running the timer.</returns>
    private async Task<bool> ApplyStoredAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetService<IIconSettingsStore>();
        if (store == null) return false;

        try
        {
            var stored = await store.LoadAsync(cancellationToken);
            stored?.ApplyTo(settings);
        }
        catch (Exception e)
        {
            // Fail open: a store blip leaves the settings as they are, which is the last known good value.
            // Refusing to render avatars, or resetting to defaults, would be a worse answer to a transient read.
            logger?.LogWarning(e, "Could not read the stored icon settings; keeping the current values.");
        }

        return true;
    }
}
