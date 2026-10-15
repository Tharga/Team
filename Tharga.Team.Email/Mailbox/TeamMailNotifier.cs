using Microsoft.Extensions.Logging;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// In-process implementation of <see cref="ITeamMailNotifier"/>; a throwing subscriber is logged and skipped, because the mail is already recorded.
/// </summary>
internal sealed class TeamMailNotifier(ILogger<TeamMailNotifier> logger = null) : ITeamMailNotifier
{
    public event EventHandler<TeamMailUpdatedEventArgs> MailUpdated;

    public void Notify(TeamMailUpdatedEventArgs args)
    {
        var handlers = MailUpdated;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<TeamMailUpdatedEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "A team-mail notification handler threw. The mail stands; only the notification failed.");
            }
        }
    }
}
