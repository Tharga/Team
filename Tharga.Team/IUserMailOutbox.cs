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

    /// <summary>Records a claimed mail as sent with its <c>Message-ID</c>; false when the claimant no longer holds it.</summary>
    Task<bool> MarkSentAsync(string id, string claimant, string messageId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Records a claimed mail as skipped for the reason given; false when the claimant no longer holds it.</summary>
    Task<bool> MarkSkippedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Records a claimed mail as given up for the reason given; false when the claimant no longer holds it.</summary>
    Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Releases a claimed mail to be tried again at <paramref name="nextAttemptAt"/>, keeping why this attempt failed; false when the claimant no longer holds it.</summary>
    Task<bool> PostponeAsync(string id, string claimant, string reason, DateTime nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>The stored mail with this id, or null.</summary>
    Task<UserMailItem> GetAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>A mail as the outbox keeps it: what was queued and where it stands.</summary>
public sealed record UserMailItem
{
    /// <summary>The mail's id.</summary>
    public required string Id { get; init; }

    /// <summary>The mail as it was queued, after sanitizing.</summary>
    public required UserMail Mail { get; init; }

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

    /// <summary>Why it was skipped or failed, or why the last attempt did not succeed.</summary>
    public string Reason { get; init; }

    /// <summary>The <c>Message-ID</c> it was sent with.</summary>
    public string MessageId { get; init; }
}
