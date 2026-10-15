namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Checks that the catch-all mailbox can be read, for a host's health endpoint.
/// </summary>
/// <remarks>
/// The poller logs a failed read and tries again, which keeps mail safe but says nothing to anyone watching the
/// service; this is what lets a health page show that mail has stopped arriving.
/// </remarks>
public interface ITeamMailboxHealth
{
    /// <summary>Connects and opens the folder read-only; never throws.</summary>
    Task<MailboxHealth> CheckAsync(CancellationToken cancellationToken = default);
}
