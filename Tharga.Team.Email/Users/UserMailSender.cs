using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>Drains the user mail queue in the background, on every instance, each mail sent by whichever instance claims it.</summary>
/// <remarks>
/// <b>Nothing is claimed until sending is configured.</b> Without a sending address or an SMTP host, mail stays queued
/// and that is logged once, so configuring it later sends what waited rather than failing it.
/// <para>
/// A scope per pass, because the stores are scoped and this is a singleton. A failed pass is logged and the next one
/// runs as usual: a claimed mail that was not recorded is taken again once its lease runs out.
/// </para>
/// </remarks>
internal sealed class UserMailSender(
    IServiceScopeFactory scopeFactory,
    IOptions<UserMailOptions> options,
    ILogger<UserMailSender> logger = null) : BackgroundService
{
    private bool _reportedNotConfigured;

    /// <summary>Who this instance claims mail as.</summary>
    internal string Claimant { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var interval = o.PollInterval > TimeSpan.Zero ? o.PollInterval : TimeSpan.FromSeconds(30);

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
                logger?.LogError(e, "A pass over the user mail queue failed. The next one will retry.");
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

    /// <summary>One pass: sends what is due, up to a batch, and returns how many mails were handled.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!CanSend()) return 0;

        using var scope = scopeFactory.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<UserMailDispatcher>().RunAsync(Claimant, cancellationToken);
    }

    /// <summary>What sending still needs, or null when the sending address and the SMTP host are both set.</summary>
    internal static string Missing(MailOptions mail)
    {
        mail ??= new MailOptions();

        return string.IsNullOrWhiteSpace(mail.FromAddress) ? "a sending address (Mail.FromAddress)"
            : !mail.Smtp.IsConfigured ? "an SMTP host (Mail.Smtp.Host)"
            : null;
    }

    private bool CanSend()
    {
        var missing = Missing(options.Value.Mail);

        if (missing == null) return true;

        if (!_reportedNotConfigured)
        {
            _reportedNotConfigured = true;
            logger?.LogWarning("Mail to users is queued but not sent: {Missing} is not configured in {Options}. Queued mail waits and is sent once it is.", missing, nameof(UserMailOptions));
        }

        return false;
    }
}
