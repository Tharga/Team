using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Keeps raised notices until they are distributed, each user's notices until they are mailed, and lets one instance at a time claim each; internal and unchecked, so raise notices through <see cref="IUserNoticeRaiser"/>, never by injecting this.</summary>
/// <remarks>
/// <b>Two claims make several instances safe.</b> An occurrence is claimed to find its recipients, and a user's
/// stream — their notices of one kind delivered at once, or their daily summary of one mail category — is claimed to
/// mail what waits in it, so no two instances send the same mail. A user's notice is stored once per occurrence, which
/// is what keeps an occurrence raised twice from reaching anyone twice. <c>Tharga.Team.MongoDB</c> registers one;
/// register another before it to keep notices elsewhere.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserNoticeOutbox
{
    /// <summary>Stores a raised occurrence as waiting to be distributed and returns its id.</summary>
    Task<string> AddAsync(UserNoticeOccurrence occurrence, DateTime raisedAt, CancellationToken cancellationToken = default);

    /// <summary>Claims the occurrence longest due at <paramref name="now"/> that nobody holds, for <paramref name="lease"/>, counting one attempt; null when there is none.</summary>
    Task<UserNoticeRecord> ClaimNextAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>Records a claimed occurrence as distributed to this many users; false when the claimant no longer holds it.</summary>
    Task<bool> MarkDistributedAsync(string id, string claimant, int recipients, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Records a claimed occurrence as given up for the reason given; false when the claimant no longer holds it.</summary>
    Task<bool> MarkFailedAsync(string id, string claimant, string reason, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Releases a claimed occurrence to be tried again at <paramref name="nextAttemptAt"/>; false when the claimant no longer holds it.</summary>
    Task<bool> PostponeAsync(string id, string claimant, string reason, DateTime nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>The stored occurrence with this id, or null.</summary>
    Task<UserNoticeRecord> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Stores a user's notice unless that user already has the same occurrence, and makes its stream due no later than <paramref name="dueBy"/> while it waits; true when it was stored now.</summary>
    Task<bool> AddItemAsync(UserNoticeItem item, DateTime dueBy, CancellationToken cancellationToken = default);

    /// <summary>Claims the stream longest due at <paramref name="now"/> that nobody holds, for <paramref name="lease"/>; null when there is none.</summary>
    Task<UserNoticeStreamClaim> ClaimDueStreamAsync(string claimant, DateTime now, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>The notices waiting in a user's stream, oldest first, at most <paramref name="max"/>.</summary>
    Task<UserNoticeItem[]> GetWaitingAsync(string userKey, string stream, int max, CancellationToken cancellationToken = default);

    /// <summary>The user's notices with these ids.</summary>
    Task<UserNoticeItem[]> GetItemsAsync(string[] ids, CancellationToken cancellationToken = default);

    /// <summary>Records on a claimed stream which notices the mail about to be queued carries, so a claimant taking over sends the same mail; false when the claimant no longer holds it.</summary>
    Task<bool> BeginMailAsync(string userKey, string stream, string claimant, string mailKey, string[] itemIds, CancellationToken cancellationToken = default);

    /// <summary>Records waiting notices as mailed or given up, with the queued mail's id or the reason.</summary>
    Task CompleteItemsAsync(string[] itemIds, UserNoticeItemStatus status, string mailId, string reason, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Releases a claimed stream, due again at <paramref name="nextDueWhenWaiting"/> if any notice still waits in it; false when the claimant no longer holds it.</summary>
    Task<bool> ReleaseStreamAsync(string userKey, string stream, string claimant, DateTime nextDueWhenWaiting, CancellationToken cancellationToken = default);
}

/// <summary>Where a raised occurrence stands.</summary>
public enum UserNoticeStatus
{
    /// <summary>Waiting to be distributed, or to be tried again.</summary>
    Pending,

    /// <summary>Its recipients found and their notices stored.</summary>
    Distributed,

    /// <summary>Given up, for the reason recorded.</summary>
    Failed
}

/// <summary>A raised occurrence as the outbox keeps it.</summary>
public sealed record UserNoticeRecord
{
    /// <summary>The occurrence's id.</summary>
    public required string Id { get; init; }

    /// <summary>The occurrence as it was raised, after checking.</summary>
    public required UserNoticeOccurrence Occurrence { get; init; }

    /// <summary>Where it stands.</summary>
    public required UserNoticeStatus Status { get; init; }

    /// <summary>How many times it has been claimed.</summary>
    public int Attempts { get; init; }

    /// <summary>When it was raised, in UTC.</summary>
    public required DateTime RaisedAt { get; init; }

    /// <summary>When it is next due, in UTC.</summary>
    public DateTime NextAttemptAt { get; init; }

    /// <summary>When it was distributed or given up, in UTC.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>How many users it was given to.</summary>
    public int Recipients { get; init; }

    /// <summary>Why it was given up, or why the last attempt did not succeed.</summary>
    public string Reason { get; init; }
}

/// <summary>Where one user's notice stands.</summary>
public enum UserNoticeItemStatus
{
    /// <summary>Waiting in its stream to be mailed.</summary>
    Waiting,

    /// <summary>Queued as, or in, a mail to the user.</summary>
    Mailed,

    /// <summary>Not mailed, because the user turned the notice off before it went.</summary>
    Skipped,

    /// <summary>Not mailed, for the reason recorded.</summary>
    Failed
}

/// <summary>One occurrence as it reaches one user.</summary>
public sealed record UserNoticeItem
{
    /// <summary>The item's id, set by the outbox.</summary>
    public string Id { get; init; }

    /// <summary>The id of the occurrence it came from.</summary>
    public required string NoticeId { get; init; }

    /// <summary>What identifies the occurrence: the notice, the subject and the discriminator.</summary>
    public required string OccurrenceKey { get; init; }

    /// <summary>The key of the user it reaches.</summary>
    public required string UserKey { get; init; }

    /// <summary>The team the occurrence belongs to.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The notice's key.</summary>
    public required string NoticeKey { get; init; }

    /// <summary>The mail category it is sent in.</summary>
    public required string Category { get; init; }

    /// <summary>The user's stream it waits in.</summary>
    public required string Stream { get; init; }

    /// <summary>How it is delivered.</summary>
    public required UserNoticeDelivery Delivery { get; init; }

    /// <summary>The id of the thing it is about.</summary>
    public required string SubjectId { get; init; }

    /// <summary>How the thing is named to a reader.</summary>
    public string SubjectText { get; init; }

    /// <summary>The placeholder values, by name.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

    /// <summary>The site path of the thing, or null.</summary>
    public string LinkPath { get; init; }

    /// <summary>When the occurrence was raised, in UTC.</summary>
    public required DateTime RaisedAt { get; init; }

    /// <summary>Where it stands.</summary>
    public UserNoticeItemStatus Status { get; init; }

    /// <summary>The id of the queued mail that carries it.</summary>
    public string MailId { get; init; }

    /// <summary>When it was mailed or given up, in UTC.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>Why it was given up.</summary>
    public string Reason { get; init; }
}

/// <summary>A user's stream as it was when claimed.</summary>
public sealed record UserNoticeStreamClaim
{
    /// <summary>The user's key.</summary>
    public required string UserKey { get; init; }

    /// <summary>The stream.</summary>
    public required string Stream { get; init; }

    /// <summary>The key of a mail a previous claimant began and did not finish, or null.</summary>
    public string MailKey { get; init; }

    /// <summary>The notices that unfinished mail carries.</summary>
    public string[] MailItemIds { get; init; } = [];
}
