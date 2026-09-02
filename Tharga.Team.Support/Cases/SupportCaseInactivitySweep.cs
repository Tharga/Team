using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Closes cases that support answered and nobody came back to.
/// </summary>
/// <remarks>
/// <b>Separated from the hosted service that runs it</b>, so the decision of *which* cases close is testable
/// without a timer. The hosted service is a loop; this is the behaviour.
/// <para>
/// <b>It runs as framework code with no caller</b>, so it goes to the store rather than through
/// <c>ISupportCaseService</c>. There is no principal to authorize and nothing to authorize it against — the
/// sweep is not acting on anybody's behalf, which is exactly the case the *Internal* category in
/// <c>shared-instructions.md</c> describes.
/// </para>
/// </remarks>
internal sealed class SupportCaseInactivitySweep(
    ISupportCaseStore store,
    IOptions<SupportCaseOptions> options,
    TimeProvider timeProvider,
    ISupportCaseNotifier notifier = null,
    ILogger<SupportCaseInactivitySweep> logger = null)
{
    /// <summary>Closes everything eligible, and reports how many it closed.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (settings.AutoCloseAfter <= TimeSpan.Zero) return 0;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - settings.AutoCloseAfter;

        var due = await store.GetCasesForInactivityCloseAsync(cutoff, settings.AutoCloseBatchSize, cancellationToken);

        var closed = 0;

        foreach (var supportCase in due)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var closure = new SupportMessage
            {
                Sequence = 0,
                Kind = SupportMessageKind.System,
                Body = $"Closed automatically after {Describe(settings.AutoCloseAfter)} without a reply. Reopen it if the problem is still there.",
                SentAt = now
            };

            // The store decides, conditionally on the case still being open, so two instances sweeping
            // together close it once. A false here is another instance having got there first, not a failure.
            if (!await store.TryCloseForInactivityAsync(supportCase.TeamKey, supportCase.Id, now, closure, cancellationToken))
                continue;

            closed++;

            notifier?.Notify(new SupportCaseUpdatedEventArgs
            {
                TeamKey = supportCase.TeamKey,
                CaseId = supportCase.Id,
                Change = SupportCaseChange.Closed,

                // Not from a channel: nobody outside the application did this, so a UI waiting for "somebody
                // answered" must not light up for it.
                FromChannel = false
            });
        }

        if (closed > 0) logger?.LogInformation("Closed {Count} support case(s) after inactivity.", closed);

        return closed;
    }

    /// <summary>
    /// The span in words, because the closure entry is read by the customer who raised the case.
    /// </summary>
    private static string Describe(TimeSpan span)
        => span.TotalDays >= 2 ? $"{span.TotalDays:0.#} days"
            : span.TotalHours >= 2 ? $"{span.TotalHours:0.#} hours"
            : span.ToString();
}
