using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Queues mail to one user for sending in the background; unchecked, so call it from server-side code acting on its own authority, never from a component, controller or MCP provider.</summary>
/// <remarks>
/// <b>Persisted before it returns</b>, so a queued mail survives a restart, and sent by whichever instance claims it
/// first. When it is sent, the recipient's choice for the category is checked and their address read from the user
/// record; a user who turned the category off in the meantime is recorded as skipped, not as failed.
/// <para>
/// Mail in an optional category carries an unsubscribe link in its footer and the <c>List-Unsubscribe</c> headers;
/// mail in a required category carries neither.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserMailQueue
{
    /// <summary>Validates, sanitizes and queues the mail, or returns the mail already queued under its deduplication key.</summary>
    /// <exception cref="ArgumentException">The category is not registered, the mail has no subject or body, or it exceeds the configured limits.</exception>
    Task<UserMailQueued> QueueAsync(UserMail mail, CancellationToken cancellationToken = default);

    /// <summary>What became of a queued mail, or null when there is none with this id.</summary>
    Task<UserMailDelivery> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Records the mail and makes one attempt at once, never retried, so whoever is waiting learns whether it went; for mail such as an invitation.</summary>
    /// <exception cref="ArgumentException">The category is not registered, the mail has no subject or body, or it exceeds the configured limits.</exception>
    Task<UserMailDelivery> SendNowAsync(UserMail mail, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(SendNowAsync)}.");

    /// <summary>Validates, sanitizes and stores the content once, then queues one mail per distinct recipient that each name it.</summary>
    /// <exception cref="ArgumentException">The batch names no recipient, the category is not registered, it has no subject or body, or it exceeds the configured limits.</exception>
    Task<UserMailBatchQueued> QueueBatchAsync(UserMailBatch batch, CancellationToken cancellationToken = default);

    /// <summary>What became of the mails of a queued batch, or null when there is none with this id.</summary>
    Task<UserMailBatchDelivery> GetBatchAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>One page of what became of each mail of a batch, in the order they were queued, only those in <paramref name="status"/> when one is given.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued for this list.</exception>
    Task<UserMailDeliveryPage> ListBatchAsync(string batchId, UserMailStatus? status = null, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>One page of the mail sent to one user, newest first: every mail queued for their key, and every mail queued to <paramref name="address"/> alone.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued for this list.</exception>
    Task<UserMailDeliveryPage> ListForRecipientAsync(string userKey, string address, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>One page of all mail to users and contacts, newest first, narrowed by <paramref name="filter"/>; without bodies and files.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued for this list.</exception>
    Task<UserMailDeliveryPage> ListAsync(UserMailFilter filter, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(ListAsync)}.");
}

/// <summary>One page of queued mail and what became of each.</summary>
public sealed record UserMailDeliveryPage
{
    /// <summary>The mails on this page.</summary>
    public UserMailDelivery[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}

/// <summary>The outcome of <see cref="IUserMailQueue.QueueAsync"/>.</summary>
/// <param name="Id">The id of the queued mail, or of the one already queued under the same deduplication key.</param>
/// <param name="Duplicate">True when a mail with the same deduplication key was already queued, so nothing new was.</param>
public sealed record UserMailQueued(string Id, bool Duplicate);

/// <summary>Where a queued mail stands.</summary>
public enum UserMailStatus
{
    /// <summary>Waiting to be sent, or to be tried again.</summary>
    Queued,

    /// <summary>Accepted by the mail server.</summary>
    Sent,

    /// <summary>Not sent, because the recipient does not receive the category.</summary>
    Skipped,

    /// <summary>Not sent and given up, for the reason recorded.</summary>
    Failed,

    /// <summary>Accepted by the mail server, then returned by the receiving one as undeliverable, for the reason it gave.</summary>
    /// <remarks>Seen only when the delivery report reaches a mailbox the toolkit reads, which is the case when the sending address is one of the support mailboxes.</remarks>
    Bounced
}

/// <summary>What became of one queued mail.</summary>
public sealed record UserMailDelivery
{
    /// <summary>The mail's id.</summary>
    public required string Id { get; init; }

    /// <summary>The key of the user the mail is for; null for a mail queued to an address alone.</summary>
    public string RecipientUserKey { get; init; }

    /// <summary>The address the mail was addressed to: the one it was queued with, or for a mail to a user the one on their record when it was sent; null until then.</summary>
    public string RecipientAddress { get; init; }

    /// <summary>The name a mail queued to an address alone is addressed to, or null.</summary>
    public string RecipientName { get; init; }

    /// <summary>The category's key.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>Which kind of mail it is.</summary>
    public UserMailKind Kind { get; init; }

    /// <summary>The deduplication key it was queued with, if any.</summary>
    public string DeduplicationKey { get; init; }

    /// <summary>The key of the team it is about, or null when it belongs to none or to several.</summary>
    public string TeamKey { get; init; }

    /// <summary>The batch whose content it carries, or null for a mail queued on its own.</summary>
    public string BatchId { get; init; }

    /// <summary>Where it stands.</summary>
    public required UserMailStatus Status { get; init; }

    /// <summary>How many times sending it has been attempted.</summary>
    public int Attempts { get; init; }

    /// <summary>When it was queued, in UTC.</summary>
    public required DateTime QueuedAt { get; init; }

    /// <summary>When it is next due to be attempted while queued, in UTC.</summary>
    public DateTime NextAttemptAt { get; init; }

    /// <summary>When it was sent, skipped or given up, in UTC; null while queued.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Why it was skipped, failed or bounced, or why the last attempt did not succeed while it waits to be tried again.</summary>
    public string Reason { get; init; }

    /// <summary>The <c>Message-ID</c> it was sent with; null until it is sent.</summary>
    public string MessageId { get; init; }

    /// <summary>When the receiving server's report that it bounced was read, in UTC; null unless it bounced.</summary>
    public DateTime? BouncedAt { get; init; }

    /// <summary>When it last changed, in UTC: when it was queued, sent, skipped, given up or reported bounced.</summary>
    public DateTime ChangedAt => BouncedAt ?? CompletedAt ?? QueuedAt;
}
