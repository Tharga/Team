using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Runs <see cref="SupportSecureFileSweep"/> on an interval.
/// </summary>
/// <remarks>
/// A scope per pass and a failed pass logged with the timer carrying on, for the reasons <see cref="SupportCaseInactivityService"/> gives.
/// Registered only when <see cref="SupportSecureFileOptions.KeepFor"/> is above zero.
/// </remarks>
internal sealed class SupportSecureFileSweepService(
    IServiceScopeFactory scopeFactory,
    IOptions<SupportCaseOptions> options,
    ILogger<SupportSecureFileSweepService> logger = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SecureFiles.SweepInterval;

        if (interval <= TimeSpan.Zero) return;

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                using var scope = scopeFactory.CreateScope();
                using var access = Tharga.Team.Service.TeamAccess.Unchecked("Support secure file sweep: background removal of secure files past their time, with no caller to authorize.");

                await scope.ServiceProvider
                    .GetRequiredService<SupportSecureFileSweep>()
                    .RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger?.LogError(e, "A support secure file sweep failed. The next one will retry.");
            }
        }
    }
}
