using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Slack;

namespace Tharga.Team.Support.Notifications;

/// <summary>
/// An audit sink that posts routed entries to Slack.
/// </summary>
/// <remarks>
/// Registered as a fourth <see cref="IAuditLogger"/>, so it receives whatever
/// <c>CompositeAuditLogger</c> fans out — already filtered by <c>AuditOptions</c> and already enriched.
/// Notifications are therefore a sink rather than a second event mechanism, and a consumer raising its
/// own entry through <see cref="IAuditEntryFactory"/> reaches Slack by the same path as the toolkit's
/// own events, with no extra registration.
/// <para>
/// <b>The audit filter sits upstream of routing.</b> An <c>AuditOptions.EventFilter</c> or
/// <c>ExcludedActions</c> that drops an entry drops the notification with it, however the route reads.
/// That is the price of reusing the seam instead of growing a parallel one, and it is asserted in the
/// tests so it stays a known property rather than a surprise.
/// </para>
/// <para>
/// <b><see cref="Log"/> never blocks and never throws.</b> It is called on the thread of the operation
/// being audited — a team being created, a member being invited — so an HTTPS round trip to Slack
/// happens on a background pump instead. A queue that has filled up drops its oldest entries: losing an
/// old notification is better than delaying the write that triggered it.
/// </para>
/// </remarks>
public sealed class SlackNotificationSink : BackgroundService, IAuditLogger
{
    private const int QueueCapacity = 1_000;

    private readonly NotificationRouter _router;
    private readonly ISlackClient _slackClient;
    private readonly ILogger<SlackNotificationSink> _logger;
    private readonly Channel<AuditEntry> _queue = Channel.CreateBounded<AuditEntry>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest });

    public SlackNotificationSink(NotificationRouter router, ISlackClient slackClient, ILogger<SlackNotificationSink> logger = null)
    {
        _router = router;
        _slackClient = slackClient;
        _logger = logger;
    }

    /// <summary>Queues an entry. Routing and posting happen on the background pump.</summary>
    public void Log(AuditEntry entry)
    {
        if (entry == null) return;
        _queue.Writer.TryWrite(entry);
    }

    /// <summary>
    /// Not a query sink. Reads are served by the audit store; this exists only to satisfy
    /// <see cref="IAuditLogger"/>.
    /// </summary>
    public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await DispatchAsync(entry, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    /// <remarks>
    /// Internal so a test can drive one entry end to end without starting a host and waiting on a pump.
    /// The pump itself is three lines around this call; the behaviour worth asserting is here.
    /// </remarks>
    internal async Task DispatchAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NotificationMessage> messages;
        try
        {
            messages = _router.Route(entry);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Routing an audit entry for notification failed; the entry is not notified.");
            return;
        }

        foreach (var message in messages)
        {
            try
            {
                var result = await _slackClient.PostAsync(message.Channel, message.Text, cancellationToken: cancellationToken);
                if (!result.Success)
                {
                    _logger?.LogWarning("Notification to Slack channel {Channel} was not delivered: {Error}", message.Channel, result.Error);
                }
            }
            catch (Exception ex)
            {
                // ISlackClient promises not to throw, but a substitute or a future transport might.
                _logger?.LogWarning(ex, "Notification to Slack channel {Channel} threw.", message.Channel);
            }
        }
    }
}
