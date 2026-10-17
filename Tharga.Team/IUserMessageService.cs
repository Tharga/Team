namespace Tharga.Team;

/// <summary>Messages the product's staff write to chosen users of its teams, and the log of every message sent; every member requires the system scope <see cref="SystemMessageScopes.Send"/>.</summary>
/// <remarks>
/// <b>Gated</b>: registered as a system service, so a call without the scope is refused before it reaches the
/// implementation and the refusal is audited. Sending and reading the log are audited by the service itself, with
/// counts and never addresses, so the per-call access trace is turned off.
/// </remarks>
public interface IUserMessageService
{
    /// <summary>The teams, users, roles, access levels and categories a message may be written to and in, and the limits it is held to.</summary>
    [RequireScope(SystemMessageScopes.Send, Audit = AuditMode.None)]
    Task<UserMessageChoices> GetChoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>How many users the selection reaches and how many of them turned the category off, counts only.</summary>
    /// <exception cref="ArgumentException">The selection names no team, or the category is not one staff may write in.</exception>
    [RequireScope(SystemMessageScopes.Send, Audit = AuditMode.None)]
    Task<UserMessagePreview> PreviewAsync(UserMessageRecipients recipients, string category, CancellationToken cancellationToken = default);

    /// <summary>Queues one mail per user the selection reaches, returns at once with the message as the log records it, and audits the send.</summary>
    /// <exception cref="ArgumentException">The selection reaches nobody, the category is not one staff may write in, or the message has no subject or body or exceeds the limits.</exception>
    [RequireScope(SystemMessageScopes.Send, Audit = AuditMode.None)]
    Task<SentUserMessage> SendAsync(UserMessage message, CancellationToken cancellationToken = default);

    /// <summary>One page of the sent log, newest first, with each message's outcome counts.</summary>
    [RequireScope(SystemMessageScopes.Send, Audit = AuditMode.None)]
    Task<SentUserMessagePage> GetSentAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>One sent message with its outcome counts and why each mail given up was, or null when there is none with this id.</summary>
    [RequireScope(SystemMessageScopes.Send, Audit = AuditMode.None)]
    Task<SentUserMessage> GetSentMessageAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>One page of a sent message's recipients in the order they were queued, each with the address it went to and what became of it, only those in <paramref name="status"/> when one is given; null when there is no message with this id.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued for this list.</exception>
    [RequireScope(SystemMessageScopes.Send, Audit = AuditMode.None)]
    Task<UserMessageRecipientPage> GetRecipientsAsync(string messageId, UserMailStatus? status = null, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);
}
