using Microsoft.Extensions.Logging;

namespace Tharga.Team.Email.Users;

/// <summary>Marks the user mail a bounce names as bounced, with the receiving server's reason; a report for a mail it does not know is left to the next handler.</summary>
/// <remarks>
/// Neither the reason nor any address is logged, because a bounce's reason often quotes the recipient's address.
/// <para>
/// The outcome sinks are told only when this report changed the mail, so a report read again, by another instance or after a restart,
/// does not tell them twice.
/// </para>
/// </remarks>
internal sealed class UserMailBounceHandler(IUserMailOutbox outbox, ILogger<UserMailBounceHandler> logger = null, MailOutcomeNotifier notifier = null) : IMailBounceHandler
{
    internal const string DefaultReason = "The receiving mail server returned the mail as undeliverable.";

    public async Task<bool> HandleAsync(MailBounce bounce, CancellationToken cancellationToken = default)
    {
        var messageId = bounce?.OriginalMessageId?.Trim().Trim('<', '>').Trim();
        if (string.IsNullOrEmpty(messageId)) return false;

        var reason = string.IsNullOrWhiteSpace(bounce.Reason) ? DefaultReason : bounce.Reason.Trim();
        var result = await outbox.MarkBouncedAsync(messageId, reason, bounce.ReceivedAt, cancellationToken);

        switch (result?.Outcome)
        {
            case UserMailBounceOutcome.Bounced:
                logger?.LogInformation("User mail {Id} bounced and is marked as not delivered.", result.Id);
                await TellAsync(result.Id, reason, bounce.ReceivedAt, cancellationToken);
                return true;
            case UserMailBounceOutcome.AlreadyBounced:
                logger?.LogDebug("User mail {Id} was already marked as bounced; the report changed nothing.", result.Id);
                return true;
            default:
                return false;
        }
    }

    private async Task TellAsync(string id, string reason, DateTime at, CancellationToken cancellationToken)
    {
        if (notifier == null || id == null) return;

        var item = await outbox.GetAsync(id, cancellationToken);
        if (item != null) await notifier.NotifyAsync(MailOutcomes.Of(item, MailOutcomeKind.Bounced, reason, at), cancellationToken);
    }
}
