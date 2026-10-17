using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Deletes mail the postmaster has held longer than <see cref="TeamMailboxOptions.PostmasterRetention"/>, its
/// content with it, and records every deletion.
/// </summary>
/// <remarks>
/// <b>Recorded one mail at a time.</b> Unrouted mail may have been an organisation's received mail, so
/// deleting it is the kind of act someone may later need to account for — which mail, and when.
/// </remarks>
internal sealed class PostmasterRetentionSweep(IServiceScopeFactory scopeFactory, IOptions<TeamMailboxOptions> options, TimeProvider time, ILogger<PostmasterRetentionSweep> logger = null) : BackgroundService
{
    internal const string Feature = "mailbox";
    internal const string Action = "postmaster.retention-delete";
    internal const string MailIdKey = "mailbox.mail.id";

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger?.LogError(e, "The postmaster retention sweep failed. The next one will try again.");
            }

            try
            {
                await Task.Delay(Interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task<string[]> SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        var cutoff = time.GetUtcNow().UtcDateTime - options.Value.PostmasterRetention;
        var deleted = await services.GetRequiredService<ITeamMailStore>().DeleteUnroutedReceivedBeforeAsync(cutoff, cancellationToken);
        if (deleted.Length == 0) return deleted;

        var content = services.GetService<IInboundMailContentStore>();
        var auditLogger = services.GetService<CompositeAuditLogger>();
        var auditEntries = services.GetService<IAuditEntryFactory>();
        using var actor = services.GetService<IAuditContextAccessor>()?.Push(new AuditActor(nameof(PostmasterRetentionSweep), CorrelationId: Guid.NewGuid()));

        foreach (var mailId in deleted)
        {
            if (content != null) await content.DeleteAsync(mailId, cancellationToken);
            if (auditLogger != null && auditEntries != null) auditLogger.Log(auditEntries.Create(AuditEventType.DataChange, Feature, Action, nameof(SweepAsync), metadata: new Dictionary<string, string> { [MailIdKey] = mailId }));
        }

        logger?.LogInformation("The postmaster deleted {Count} mail held longer than {Retention}.", deleted.Length, options.Value.PostmasterRetention);

        return deleted;
    }
}
