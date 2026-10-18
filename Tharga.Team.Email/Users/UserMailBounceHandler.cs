using Microsoft.Extensions.Logging;

namespace Tharga.Team.Email.Users;

/// <summary>Marks the user mail a bounce names as bounced, with the receiving server's reason; a report for a mail it does not know is left to the next handler.</summary>
/// <remarks>Neither the reason nor any address is logged, because a bounce's reason often quotes the recipient's address.</remarks>
internal sealed class UserMailBounceHandler(IUserMailOutbox outbox, ILogger<UserMailBounceHandler> logger = null) : IMailBounceHandler
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
                return true;
            case UserMailBounceOutcome.AlreadyBounced:
                logger?.LogDebug("User mail {Id} was already marked as bounced; the report changed nothing.", result.Id);
                return true;
            default:
                return false;
        }
    }
}
