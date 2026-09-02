using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Touches the support collections at startup, so the first real request does not pay for creating them.
/// </summary>
/// <remarks>
/// <b>Measured, not guessed.</b> The first inbound Slack event after a restart took **3434 ms** against a
/// warm 43–57 ms (2026-09-02). The difference is first-touch index assurance on <c>SupportEventLedger</c> —
/// a unique index and a TTL index on a collection that may not exist yet — and on <c>SupportCase</c>.
/// <para>
/// <b>Slack allows three seconds.</b> Over that it treats the delivery as failed and retries, and repeated
/// failures get an event subscription disabled. Nothing is lost when it retries, because
/// <see cref="ISupportEventLedger"/> makes a redelivery idempotent — which is the real reason the inbound
/// path is safe without acknowledging before it writes. This removes the failed delivery per deployment
/// rather than the risk of losing a message, and it is worth being clear about which.
/// </para>
/// <para>
/// <b>Failure here is not a failure.</b> A database that is briefly unreachable at startup must not stop the
/// host: the first request then pays the cost it would have paid anyway. So everything is caught and logged
/// at debug.
/// </para>
/// <para>
/// <b>It does not delay startup.</b> The work happens on a background task rather than in
/// <see cref="StartAsync"/>, because a host waiting on a warm-up would have traded a slow first request for
/// a slow deployment.
/// </para>
/// </remarks>
internal sealed class SupportCollectionWarmUp(
    IServiceScopeFactory scopeFactory,
    ILogger<SupportCollectionWarmUp> logger = null) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => WarmAsync(cancellationToken), CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task WarmAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();

            // A read that matches nothing is enough: the cost being avoided is opening the collection and
            // assuring its indexes, not the query.
            var cases = scope.ServiceProvider.GetService<ISupportCaseRepositoryCollection>();
            if (cases != null)
                await cases.GetOneAsync(Builders<SupportCaseEntity>.Filter.Eq(x => x.CaseId, string.Empty));

            var ledger = scope.ServiceProvider.GetService<ISupportEventLedgerCollection>();
            if (ledger != null)
                await ledger.GetOneAsync(Builders<SupportEventLedgerEntity>.Filter.Eq(x => x.EventId, string.Empty));

            logger?.LogDebug("Support collections are warm.");
        }
        catch (Exception e)
        {
            logger?.LogDebug(e, "Warming the support collections failed. The first request will pay for it instead.");
        }
    }
}
