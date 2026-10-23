using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Checks that the support mailboxes can be read and sent from, for a host's health endpoint.
/// </summary>
/// <remarks>
/// The poller logs a failed read and tries again, and an answer that cannot go out is recorded on its case; neither says
/// anything to anyone watching the service. This is what lets a health page show that mail to support has stopped arriving
/// or that answers cannot be sent.
/// </remarks>
public interface ISupportMailboxHealth
{
    /// <summary>
    /// Connects to every mailbox that has an IMAP or SMTP server set, the primary first, and returns one result for each:
    /// the IMAP folder opened read-only and the SMTP server signed in to, nothing read and nothing sent. Never throws, and
    /// is empty when no mailbox is configured.
    /// </summary>
    Task<IReadOnlyList<SupportMailboxHealth>> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>Whether one support mailbox can be read and sent from.</summary>
/// <param name="Name">The name its cases store: <see cref="SupportCaseOptions.PrimaryMailboxName"/> for the primary, otherwise the name it was added under.</param>
/// <param name="Reading">Whether its IMAP folder can be opened; not configured for a mailbox that only sends.</param>
/// <param name="Sending">Whether its SMTP server can be reached and signed in to; not configured for a mailbox that only reads.</param>
public sealed record SupportMailboxHealth(string Name, MailboxHealth Reading, MailboxHealth Sending)
{
    /// <summary>Whether everything configured on the mailbox works.</summary>
    public bool IsHealthy => (!Reading.IsConfigured || Reading.IsReachable) && (!Sending.IsConfigured || Sending.IsReachable);
}
