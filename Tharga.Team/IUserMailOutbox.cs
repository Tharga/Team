using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Keeps queued user mail until it is sent, and lets one sender at a time claim each mail; internal and unchecked, so queue mail through <see cref="IUserMailQueue"/>, never by injecting this.</summary>
/// <remarks>
/// <b>Claiming is the one atomic step that makes several instances safe.</b> A claim takes a due mail nobody holds,
/// or one whose holder's lease has run out, and counts the attempt in the same write. Every later write names the
/// claimant and is refused when another has taken the mail since, so a sender whose lease ran out cannot record over
/// the one that took over. <c>Tharga.Team.MongoDB</c> registers one; register another before it to keep mail elsewhere.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserMailOutbox
{
    /// <summary>Stores a mail as queued and due at once, or returns the one already stored under its deduplication key.</summary>
    Task<UserMailQueued> AddAsync(UserMail mail, DateTime queuedAt, CancellationToken cancellationToken = default);

    /// <summary>Claims the queued mail longest due at <paramref name="now"/> that nobody holds, for <paramref name="lease"/>, counting one attempt; null when there is none.</summary>
    Task<UserMailItem> ClaimNextAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>Records a claimed mail as sent with its <c>Message-ID</c> and the address it was addressed to; false when the claimant no longer holds it.</summary>
    Task<bool> MarkSentAsync(string id, string claimant, string messageId, string address, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Records a claimed mail as skipped for the reason given; false when the claimant no longer holds it.</summary>
    Task<bool> MarkSkippedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Records a claimed mail as given up for the reason given; false when the claimant no longer holds it.</summary>
    Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Releases a claimed mail to be tried again at <paramref name="nextAttemptAt"/>, keeping why this attempt failed; false when the claimant no longer holds it.</summary>
    Task<bool> PostponeAsync(string id, string claimant, string reason, DateTime nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>The stored mail with this id, or null.</summary>
    Task<UserMailItem> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Stores the batch's content once and one queued mail, due at once, per recipient it names — each user key, then each address alone — and returns the batch's id.</summary>
    Task<string> AddBatchAsync(UserMailBatch batch, DateTime queuedAt, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(AddBatchAsync)}. Implement it to queue one mail to many users.");

    /// <summary>The content of a batch, its files without their bytes once every mail of it is done, or null when there is no such batch.</summary>
    Task<UserMailBatch> GetBatchContentAsync(string batchId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(GetBatchContentAsync)}. Implement it to send one mail to many users.");

    /// <summary>How many mails of a batch stand where and why those given up were, or null when there is no such batch.</summary>
    Task<UserMailBatchDelivery> GetBatchDeliveryAsync(string batchId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(GetBatchDeliveryAsync)}. Implement it to report on one mail to many users.");

    /// <summary>One page of the mails of a batch in the order they were queued, only those in <paramref name="status"/> when one is given, after the cursor a previous page returned; without their files' bytes.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued by this outbox.</exception>
    Task<UserMailItemPage> ListBatchAsync(string batchId, UserMailStatus? status, string cursor, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(ListBatchAsync)}. Implement it to list the recipients of one mail to many users.");

    /// <summary>One page, newest first, of the mails queued for this user's key or to this address with no user key, after the cursor a previous page returned; without their files' bytes.</summary>
    /// <remarks>The address is compared as given, which the queue stores trimmed and in lower case; a null address matches nothing. Contact mail is left out: it went to the address before, or beside, it was a user's.</remarks>
    /// <exception cref="ArgumentException">The cursor was not issued by this outbox.</exception>
    Task<UserMailItemPage> ListForRecipientAsync(string userKey, string address, string cursor, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(ListForRecipientAsync)}. Implement it to show a user the mail sent to them.");

    /// <summary>Stores a mail already claimed by <paramref name="claimant"/> for <paramref name="lease"/>, its first attempt counted, so it is sent at once by the caller and by nobody else unless the lease runs out.</summary>
    /// <exception cref="ArgumentException">The mail carries a deduplication key, which a mail sent at once does not take.</exception>
    Task<UserMailItem> AddClaimedAsync(UserMail mail, DateTime queuedAt, string claimant, TimeSpan lease, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(AddClaimedAsync)}. Implement it to send invitations as mail to users.");

    /// <summary>Records the sent mail with this <c>Message-ID</c> as bounced, with the receiving server's reason, in one conditional write, and keeps a done batch's counts true.</summary>
    /// <remarks>Only a sent mail becomes bounced, so a second report for the same mail changes nothing and says so.</remarks>
    Task<UserMailBounceResult> MarkBouncedAsync(string messageId, string reason, DateTime at, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(MarkBouncedAsync)}. Implement it to record mail to users that bounced.");

    /// <summary>For each category, the mails sent to a user — sent or bounced — completed at or after <paramref name="since"/>, and how many users received at least one, counted in one query on the server.</summary>
    /// <remarks>A category nothing was sent in is left out. Mail to an address alone is not counted.</remarks>
    Task<IReadOnlyList<UserMailCategorySent>> CountSentByCategoryAsync(DateTime since, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(CountSentByCategoryAsync)}. Implement it to tell users how often each category is sent.");

    /// <summary>For each contact category, the contact mails sent — sent or bounced — completed at or after <paramref name="since"/>, and how many distinct addresses received at least one, counted in one query on the server.</summary>
    /// <remarks>A category nothing was sent in is left out.</remarks>
    Task<IReadOnlyList<UserMailCategorySent>> CountSentToContactsByCategoryAsync(DateTime since, CancellationToken cancellationToken = default)
        => throw new NotSupportedException($"'{GetType().Name}' does not implement {nameof(CountSentToContactsByCategoryAsync)}. Implement it to tell contacts how often each category is sent.");

    /// <summary>How long a mail is kept once it is done, which bounds how far back <see cref="CountSentByCategoryAsync"/> can count; null when it is kept for good.</summary>
    TimeSpan? Retention => null;
}

/// <summary>What a bounce report did to the mail it names.</summary>
/// <param name="Outcome">Whether a mail was found, and whether this report changed it.</param>
/// <param name="Id">The id of the mail it names, or null when none is known.</param>
public sealed record UserMailBounceResult(UserMailBounceOutcome Outcome, string Id = null);

/// <summary>Whether a bounce report named a mail to a user, and whether it changed it.</summary>
public enum UserMailBounceOutcome
{
    /// <summary>No mail to a user was sent with that <c>Message-ID</c>.</summary>
    Unknown,

    /// <summary>The mail was sent and is now recorded as bounced.</summary>
    Bounced,

    /// <summary>The mail was already recorded as bounced, so nothing changed.</summary>
    AlreadyBounced
}

/// <summary>One page of stored mail.</summary>
public sealed record UserMailItemPage
{
    /// <summary>The mails on this page.</summary>
    public UserMailItem[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}

/// <summary>A mail as the outbox keeps it: what was queued and where it stands.</summary>
public sealed record UserMailItem
{
    /// <summary>The mail's id.</summary>
    public required string Id { get; init; }

    /// <summary>The mail as it was queued, after sanitizing; for a mail of a batch only its recipient, category and subject, the rest being the batch's.</summary>
    public required UserMail Mail { get; init; }

    /// <summary>The batch whose content the mail carries, or null for a mail queued on its own.</summary>
    public string BatchId { get; init; }

    /// <summary>The address the mail was addressed to: the one it was queued with, or for a mail to a user the one on their record when it was sent; null until then.</summary>
    public string RecipientAddress { get; init; }

    /// <summary>Where it stands.</summary>
    public required UserMailStatus Status { get; init; }

    /// <summary>How many times it has been claimed for sending.</summary>
    public int Attempts { get; init; }

    /// <summary>When it was queued, in UTC.</summary>
    public required DateTime QueuedAt { get; init; }

    /// <summary>When it is next due, in UTC.</summary>
    public DateTime NextAttemptAt { get; init; }

    /// <summary>Who holds it, while a sender does.</summary>
    public string ClaimedBy { get; init; }

    /// <summary>When the holder's lease runs out, in UTC.</summary>
    public DateTime? ClaimedUntil { get; init; }

    /// <summary>When it was sent, skipped or given up, in UTC.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Why it was skipped, failed or bounced, or why the last attempt did not succeed.</summary>
    public string Reason { get; init; }

    /// <summary>The <c>Message-ID</c> it was sent with.</summary>
    public string MessageId { get; init; }

    /// <summary>When the report that it bounced was read, in UTC; null unless it bounced.</summary>
    public DateTime? BouncedAt { get; init; }
}
