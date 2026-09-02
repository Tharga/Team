using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Runs <see cref="SupportCaseInactivitySweep"/> on an interval.
/// </summary>
/// <remarks>
/// <b>A scope per sweep.</b> The store is scoped, and this is a singleton — resolving one at construction
/// would capture it for the life of the application, which is the captive dependency that has already taken
/// this repo's sample down once. A scope per pass also means a fault in one sweep leaves nothing half-used
/// behind it.
/// <para>
/// <b>Registered only when the feature is on.</b> A host with <c>AutoCloseAfter</c> at zero runs no timer and
/// no query — see <c>SupportRegistration</c>.
/// </para>
/// <para>
/// <b>A failed sweep is logged and the timer continues.</b> The database being briefly unreachable must not
/// stop the host, and the next pass finds the same cases: the work is idempotent by construction, because
/// closing is conditional on the case still being open.
/// </para>
/// </remarks>
internal sealed class SupportCaseInactivityService(
    IServiceScopeFactory scopeFactory,
    IOptions<SupportCaseOptions> options,
    ILogger<SupportCaseInactivityService> logger = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.AutoCloseSweepInterval;

        if (interval <= TimeSpan.Zero) return;

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                using var scope = scopeFactory.CreateScope();

                await scope.ServiceProvider
                    .GetRequiredService<SupportCaseInactivitySweep>()
                    .RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger?.LogError(e, "A support case inactivity sweep failed. The next one will retry.");
            }
        }
    }
}
