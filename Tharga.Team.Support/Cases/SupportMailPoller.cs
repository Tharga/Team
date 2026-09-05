using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Reads the support mailbox on an interval and applies what arrives.
/// </summary>
/// <remarks>
/// <b>A loop around <see cref="EmailEventHandler"/>, and deliberately nothing more.</b> Every decision that
/// can lose or leak mail lives in the handler, which is testable as a pure sequence. What is here is the
/// timer, the position and the shape of a failure — so this file has no rules in it to get wrong.
/// <para>
/// <b>Polling rather than a webhook, decided in the spec.</b> There is no public endpoint to expose, nothing
/// to sign and nothing to verify, and the mailbox is read read-only so two applications can share it. The
/// cost is latency bounded by <see cref="MailOptions.PollInterval"/>.
/// </para>
/// <para>
/// <b>The position is kept per deployment, not per mailbox.</b> Two sites sharing one <c>support@</c> address
/// must not share a position, or the first to poll advances past a message addressed to the second and the
/// second never sees it. The key is derived from the recipients this instance answers for, which is exactly
/// what makes the two different — see <see cref="PositionKey"/>.
/// </para>
/// <para>
/// <b>It never sets <c>\Seen</c> and never moves a message.</b> That is the transport's guarantee rather than
/// this class's, but it is the reason a position exists at all: mailbox flags are shared state, and using one
/// as "handled" hides mail from the instance that wanted it.
/// </para>
/// </remarks>
internal sealed class SupportMailPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<MailOptions> options,
    ILogger<SupportMailPoller> logger = null) : BackgroundService
{
    private SupportMailPosition _position = SupportMailPosition.Start;
    private bool _positionLoaded;
    private bool _warnedAboutMissingStore;

    /// <summary>
    /// Identifies this deployment's read position.
    /// </summary>
    /// <remarks>
    /// <b>Derived rather than configured, so it cannot be forgotten.</b> An option would be one more thing to
    /// set correctly, and setting it wrong loses mail silently. The recipients an instance answers for are
    /// what distinguish it from the other instance reading the same mailbox, so they are the key.
    /// <para>
    /// <b>Two deployments answering for the same recipients share a position on purpose.</b> They would
    /// handle the same mail, and the ledger already stops the second from applying it twice — so sharing
    /// saves the work rather than losing anything.
    /// </para>
    /// </remarks>
    internal static string PositionKey(MailOptions options)
    {
        var recipients = options.Recipients ?? [];

        var scope = recipients.Length == 0
            ? "all"
            : string.Join(",", recipients.Select(x => x?.Trim().ToLowerInvariant()).Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x));

        return $"{options.Folder ?? "INBOX"}|{scope}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.PollInterval;

        if (interval <= TimeSpan.Zero)
        {
            logger?.LogInformation("The support mailbox is not polled: the interval is not positive.");
            return;
        }

        // One interval before the first read, so a deployment is not doing IMAP work while it is still
        // warming up -- and so a crash loop cannot become a poll loop against somebody's mail server.
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;

                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                // The loop outlives any single failure. A mail server that is down, a database that is
                // unreachable: the next tick tries again, and the position means nothing is skipped.
                logger?.LogError(e, "A support mailbox poll failed. The next one will retry from the same position.");
            }
        }
    }

    /// <remarks>
    /// <b>A scope per poll, because everything it touches is scoped.</b> The store, the ledger and the mail
    /// client all live per request in the ordinary graph, and a background service holding one of them for
    /// the process lifetime is the classic captive-dependency bug.
    /// </remarks>
    internal async Task<int> PollAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var client = scope.ServiceProvider.GetService<ISupportMailClient>();

        if (client == null || !client.CanRead) return 0;

        var handler = scope.ServiceProvider.GetRequiredService<EmailEventHandler>();
        var positions = scope.ServiceProvider.GetService<ISupportMailPositionStore>();
        var key = PositionKey(options.Value);

        if (!_positionLoaded)
        {
            _position = positions == null ? SupportMailPosition.Start : await positions.GetAsync(key, cancellationToken);
            _positionLoaded = true;

            if (positions == null && !_warnedAboutMissingStore)
            {
                _warnedAboutMissingStore = true;

                logger?.LogWarning(
                    "No {Store} is registered, so the support mailbox position is kept in memory only and the mailbox is re-read after a restart. " +
                    "Nothing is applied twice -- the event ledger prevents that -- but the work is repeated.",
                    nameof(ISupportMailPositionStore));
            }
        }

        var result = await client.FetchAsync(new MailFetchPosition(_position.UidValidity, _position.LastUid), cancellationToken);

        if (result.Rescanned)
        {
            logger?.LogInformation(
                "The support mailbox reported a new UID generation, so the stored position was discarded and it is being read again. " +
                "Mail already handled is recognised by the event ledger.");
        }

        var applied = 0;

        foreach (var mail in result.Mails)
        {
            // Per message, so one that cannot be handled does not hold back the position past the others.
            // The position still advances -- the fetch already decided which messages this poll covers, and
            // holding it back would re-read the whole batch to hit the same message again.
            try
            {
                var outcome = await handler.HandleAsync(mail, cancellationToken);

                if (outcome.WasApplied) applied++;
            }
            catch (Exception e)
            {
                logger?.LogError(e, "A received mail could not be applied to a support case.");
            }
        }

        var moved = result.Position.LastUid != _position.LastUid || result.Position.UidValidity != _position.UidValidity;

        _position = new SupportMailPosition(result.Position.UidValidity, result.Position.LastUid);

        // Written after the mail is handled, never before. The other order would advance past a message this
        // instance had not yet applied, and a crash in between would lose it.
        if (moved && positions != null) await positions.SetAsync(key, _position, cancellationToken);

        if (result.Mails.Count > 0)
            logger?.LogDebug("Read {Count} mail from the support mailbox, {Applied} of which reached a case.", result.Mails.Count, applied);

        return applied;
    }
}
