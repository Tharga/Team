using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>Distributes raised notices and mails what is due, in the background on every instance, each step taken by whichever instance claims it.</summary>
/// <remarks>A scope per pass, because the stores are scoped and this is a singleton; a failed pass is logged and the next one runs as usual.</remarks>
internal sealed class UserNoticeSender(
    IServiceScopeFactory scopeFactory,
    IOptions<UserNoticeOptions> options,
    ILogger<UserNoticeSender> logger = null) : BackgroundService
{
    /// <summary>Who this instance claims notices as.</summary>
    internal string Claimant { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var interval = o.PollInterval > TimeSpan.Zero ? o.PollInterval : TimeSpan.FromSeconds(15);

        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger?.LogError(e, "A pass over automatic notices failed. The next one will retry.");
            }

            if (handled >= Math.Max(1, o.BatchSize)) continue;

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass: distributes and mails what is due, up to a batch of each, and returns how many were handled.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<UserNoticeDistributor>().RunAsync(Claimant, cancellationToken);
    }
}
