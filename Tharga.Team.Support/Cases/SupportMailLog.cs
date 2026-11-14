using Microsoft.Extensions.Logging;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Support.Cases;

/// <summary>Writes support mail to the mail log, so it is listed beside mail to users, without the log ever stopping or changing the mail it describes.</summary>
/// <remarks>
/// Optional throughout: with no outbox registered nothing is written, and a write that fails is logged and goes no further.
/// The <see cref="IMailOutcomeSink"/>s are told of each attempt recorded as failed, and of a bounce only when it changed the row.
/// </remarks>
internal static class SupportMailLog
{
    /// <summary>Records the mail <paramref name="record"/> describes, and tells the outcome sinks when it was recorded as failed; building it is part of the attempt, so a failure there is caught too.</summary>
    public static async Task RecordAsync(IUserMailOutbox outbox, MailOutcomeNotifier outcomes, Func<Task<UserMailRecord>> record, ILogger logger, string caseId, CancellationToken cancellationToken)
    {
        if (outbox == null) return;

        UserMailRecord recorded;
        string id;
        try
        {
            recorded = await record();
            id = await outbox.RecordAsync(recorded, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "A mail on support case {CaseId} could not be recorded in the mail log; the mail itself is unaffected.", caseId);
            return;
        }

        if (recorded.Status == UserMailStatus.Failed && outcomes != null)
            await outcomes.NotifyAsync(Failed(id, recorded), cancellationToken);
    }

    /// <summary>Marks the logged mail with this <c>Message-ID</c> as bounced, and tells the outcome sinks when this changed it, with <paramref name="known"/> when the row cannot be read back; a mail already marked is left as it is.</summary>
    public static async Task MarkBouncedAsync(IUserMailOutbox outbox, MailOutcomeNotifier outcomes, string messageId, string reason, DateTime at, MailOutcome known, ILogger logger, string caseId, CancellationToken cancellationToken)
    {
        if (outbox == null) return;

        UserMailBounceResult result;
        try
        {
            result = await outbox.MarkBouncedAsync(messageId, reason, at, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "A bounced answer on support case {CaseId} could not be marked in the mail log; the answer is still marked as not delivered.", caseId);
            return;
        }

        if (result?.Outcome == UserMailBounceOutcome.Bounced && outcomes != null)
            await outcomes.NotifyBouncedAsync(outbox, result.Id, reason, at, known == null ? null : known with { Id = result.Id }, cancellationToken);
    }

    private static MailOutcome Failed(string id, UserMailRecord record) => new()
    {
        Id = id,
        Kind = record.Kind,
        Status = UserMailStatus.Failed,
        Reason = record.Reason,
        SentByUserKey = record.SentByUserKey,
        TeamKey = record.TeamKey,
        CaseId = record.CaseId,
        CaseMessageSequence = record.CaseMessageSequence,
        At = record.At
    };
}
