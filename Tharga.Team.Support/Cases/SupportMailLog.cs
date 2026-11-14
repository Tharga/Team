using Microsoft.Extensions.Logging;

namespace Tharga.Team.Support.Cases;

/// <summary>Writes support mail to the mail log, so it is listed beside mail to users, without the log ever stopping or changing the mail it describes.</summary>
/// <remarks>Optional throughout: with no outbox registered nothing is written, and a write that fails is logged and goes no further.</remarks>
internal static class SupportMailLog
{
    /// <summary>Records the mail <paramref name="record"/> describes; building it is part of the attempt, so a failure there is caught too.</summary>
    public static async Task RecordAsync(IUserMailOutbox outbox, Func<Task<UserMailRecord>> record, ILogger logger, string caseId, CancellationToken cancellationToken)
    {
        if (outbox == null) return;

        try
        {
            await outbox.RecordAsync(await record(), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "A mail on support case {CaseId} could not be recorded in the mail log; the mail itself is unaffected.", caseId);
        }
    }

    /// <summary>Marks the logged mail with this <c>Message-ID</c> as bounced; a mail already marked is left as it is.</summary>
    public static async Task MarkBouncedAsync(IUserMailOutbox outbox, string messageId, string reason, DateTime at, ILogger logger, string caseId, CancellationToken cancellationToken)
    {
        if (outbox == null) return;

        try
        {
            await outbox.MarkBouncedAsync(messageId, reason, at, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "A bounced answer on support case {CaseId} could not be marked in the mail log; the answer is still marked as not delivered.", caseId);
        }
    }
}
