using Microsoft.Extensions.Logging;

namespace Tharga.Team.Email.Users;

/// <summary>Tells every registered <see cref="IMailOutcomeSink"/> that a logged mail was given up or bounced; with none registered it does nothing.</summary>
/// <remarks>A sink that throws is logged and the others are still told, so nothing here can change or fail the mail.</remarks>
internal sealed class MailOutcomeNotifier(IEnumerable<IMailOutcomeSink> sinks, ILogger<MailOutcomeNotifier> logger = null)
{
    private readonly IMailOutcomeSink[] _sinks = [.. sinks ?? []];

    public async Task NotifyAsync(MailOutcome outcome, CancellationToken cancellationToken)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.OnMailOutcomeAsync(outcome, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(e, "The mail outcome sink {Sink} failed for mail {Id}; the mail and the other sinks are unaffected.", sink.GetType().Name, outcome.Id);
            }
        }
    }

    /// <summary>Tells every sink that the logged mail with this id bounced, with its facts read back from the log, or with <paramref name="known"/> when the row cannot be read.</summary>
    public async Task NotifyBouncedAsync(IUserMailOutbox outbox, string id, string reason, DateTime at, MailOutcome known, CancellationToken cancellationToken)
    {
        if (_sinks.Length == 0) return;

        var item = await ReadAsync(outbox, id, cancellationToken);
        var outcome = item != null ? Of(item, UserMailStatus.Bounced, reason, at) : known;
        if (outcome == null)
        {
            logger?.LogWarning("Mail {Id} bounced, but its row could not be read back, so no outcome sink was told.", id);
            return;
        }

        await NotifyAsync(outcome, cancellationToken);
    }

    private async Task<UserMailItem> ReadAsync(IUserMailOutbox outbox, string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(id)) return null;

        try
        {
            return await outbox.GetAsync(id, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "Mail {Id} bounced, but its row could not be read back for the outcome sinks.", id);
            return null;
        }
    }

    /// <summary>The outcome of the logged mail <paramref name="item"/>, as <paramref name="status"/> for <paramref name="reason"/> at <paramref name="at"/>.</summary>
    public static MailOutcome Of(UserMailItem item, UserMailStatus status, string reason, DateTime at) => new()
    {
        Id = item.Id,
        Kind = item.Mail?.Kind ?? UserMailKind.Mail,
        Status = status,
        Reason = reason,
        SentByUserKey = item.Mail?.SentByUserKey,
        TeamKey = item.Mail?.TeamKey,
        CaseId = item.CaseId,
        CaseMessageSequence = item.CaseMessageSequence,
        At = at
    };
}
