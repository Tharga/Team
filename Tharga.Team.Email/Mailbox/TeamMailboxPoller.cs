using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Reads the catch-all mailbox on an interval and hands every new mail to <see cref="TeamMailReceiver"/>.
/// </summary>
/// <remarks>
/// <b>Every instance polls; none needs to coordinate.</b> Each mail is kept once whoever reads it, so two
/// instances reading the same mail cost a repeated read and nothing else.
/// <para>
/// <b>A mail that fails holds the position.</b> Unlike support's poller, which moves on, this one stops at a
/// mail it could not keep and reads it again next time: mail that reached an organisation may already count
/// as received by it, so it is retried rather than skipped. The mails before it in the batch are kept.
/// </para>
/// </remarks>
internal sealed class TeamMailboxPoller(IServiceScopeFactory scopeFactory, IOptions<TeamMailboxOptions> options, ILogger<TeamMailboxPoller> logger = null) : BackgroundService
{
    internal const string PositionKeyPrefix = "team-mailbox|";

    private SupportMailPosition _position;
    private bool _positionLoaded;

    internal static string PositionKey(TeamMailboxOptions options) => $"{PositionKeyPrefix}{options.Mail?.Folder ?? "INBOX"}";

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            if (scope.ServiceProvider.GetService<IInboundMailContentStore>() == null)
                throw new InvalidOperationException($"The team mailbox reads mail, but no {nameof(IInboundMailContentStore)} is registered to keep it. Register one, or leave the mailbox's IMAP settings empty.");
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Mail?.PollInterval ?? TimeSpan.FromMinutes(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger?.LogError(e, "A team mailbox poll failed. The next one will retry from the same position.");
            }

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

    internal async Task<int> PollAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var source = scope.ServiceProvider.GetRequiredService<IMailboxSource>();
        if (!source.CanRead) return 0;

        var receiver = scope.ServiceProvider.GetRequiredService<TeamMailReceiver>();
        var positions = scope.ServiceProvider.GetService<ISupportMailPositionStore>();
        var key = PositionKey(options.Value);

        if (!_positionLoaded)
        {
            _position = positions == null ? SupportMailPosition.Start : await positions.GetAsync(key, cancellationToken);
            _positionLoaded = true;
        }

        var result = await source.FetchAsync(new MailFetchPosition(_position.UidValidity, _position.LastUid), cancellationToken);
        var reached = new SupportMailPosition(result.Position.UidValidity, result.Position.LastUid);
        var kept = 0;

        foreach (var mail in result.Mails)
        {
            try
            {
                if (await receiver.ReceiveAsync(mail, cancellationToken)) kept++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger?.LogError(e, "A received mail could not be kept; the mailbox will be read again from it.");
                reached = new SupportMailPosition(result.Position.UidValidity, mail.Uid == 0 ? _position.LastUid : mail.Uid - 1);
                break;
            }
        }

        var moved = reached != _position;
        _position = reached;

        if (moved && positions != null) await positions.SetAsync(key, _position, cancellationToken);

        return kept;
    }
}
