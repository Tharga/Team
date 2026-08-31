using Microsoft.Extensions.Logging;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// In-process implementation of <see cref="ISupportCaseNotifier"/>.
/// </summary>
/// <remarks>
/// <b>In-process, so it reaches this instance only.</b> A host running several instances will see the
/// notification on whichever one handled the change: a Slack reply wakes the instance whose endpoint Slack
/// happened to reach, not the one holding the user's circuit. Making it reach every instance means a backplane
/// -- the same problem <c>ITeamCache</c> solves for claims -- and that is not built here because nothing has
/// asked for it. Say so rather than let a consumer discover it.
/// <para>
/// <b>A throwing subscriber must not break the operation that raised the event.</b> The case is already
/// written by this point; a host handler that fails is logged and skipped, exactly as a throwing audit
/// enricher is.
/// </para>
/// </remarks>
internal sealed class SupportCaseNotifier(ILogger<SupportCaseNotifier> logger = null) : ISupportCaseNotifier
{
    public event EventHandler<SupportCaseUpdatedEventArgs> CaseUpdated;

    public void Notify(SupportCaseUpdatedEventArgs args)
    {
        var handlers = CaseUpdated;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<SupportCaseUpdatedEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "A support-case notification handler threw. The case change stands; only the notification failed.");
            }
        }
    }
}
