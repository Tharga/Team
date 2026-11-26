using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Removes the content of secure files once <see cref="SupportSecureFileOptions.KeepFor"/> has passed, leaving each entry saying its file
/// was removed.
/// </summary>
/// <remarks>
/// <b>Separated from the hosted service that runs it</b>, as <see cref="SupportCaseInactivitySweep"/> is, so which files go is testable
/// without a timer. It runs as framework code with no caller, so it goes to the stores rather than through <c>ISupportCaseService</c>.
/// <para>
/// <b>Content first, then the record, and the record is conditional.</b> Deleting content twice is nothing, so two instances sweeping
/// together both delete and exactly one records the removal. A failure between the two leaves a file that no longer opens and is still
/// due, so the next pass records it; never a "removed" mark over content that is still kept.
/// </para>
/// <para>
/// <b>Logged, not audited.</b> Nobody decided this removal; the option did. Support removing a file earlier is audited, as a decision.
/// </para>
/// </remarks>
internal sealed class SupportSecureFileSweep(
    ISupportCaseStore store,
    IOptions<SupportCaseOptions> options,
    TimeProvider timeProvider,
    ISupportAttachmentStore attachmentStore = null,
    ISupportCaseNotifier notifier = null,
    ILogger<SupportSecureFileSweep> logger = null)
{
    /// <summary>Removes every secure file that is due, up to the batch size, and reports how many this pass recorded.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value.SecureFiles;

        if (settings.KeepFor <= TimeSpan.Zero || attachmentStore == null) return 0;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await store.GetSecureAttachmentsDueAsync(now - settings.KeepFor, settings.SweepBatchSize, cancellationToken);

        var removed = 0;

        foreach (var file in due)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await attachmentStore.DeleteAsync(file.CaseId, file.AttachmentId, cancellationToken);

                // A false here is another instance having recorded it first, not a failure.
                if (!await store.TryMarkAttachmentRemovedAsync(file.CaseId, file.Sequence, file.AttachmentId, now, cancellationToken)) continue;
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One file that cannot be removed must not hold back the rest of the batch; it is still due, so the next pass tries again.
                logger?.LogError(e, "Could not remove secure file {AttachmentId} of support case {CaseId}. The next sweep will retry.", file.AttachmentId, file.CaseId);
                continue;
            }

            removed++;

            notifier?.Notify(new SupportCaseUpdatedEventArgs
            {
                TeamKey = file.TeamKey,
                CaseId = file.CaseId,
                Change = SupportCaseChange.AttachmentRemoved,
                FromChannel = false
            });
        }

        if (removed > 0) logger?.LogInformation("Removed {Count} secure support file(s) kept longer than {KeepFor}.", removed, settings.KeepFor);

        return removed;
    }
}
