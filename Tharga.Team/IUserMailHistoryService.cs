namespace Tharga.Team;

/// <summary>The mail sent to one user — messages, notices, mail queued on its own and invitations — read by that user or by staff holding <see cref="SystemUserScopes.Manage"/>.</summary>
/// <remarks>
/// <b>Checked in the service, not by scope attributes</b>, because the rule is "your own, or anyone's with
/// <c>users:manage</c>", which a scope alone cannot say. A caller who is neither is refused with
/// <see cref="UnauthorizedAccessException"/>, and the refusal is audited; staff reading someone else's history is
/// audited too, with how many mails were read and never an address.
/// <para>
/// A user's history holds every mail queued for their key, and every mail queued to their current address alone,
/// such as the invitation that brought them in. It reaches back as far as the queue keeps mail.
/// </para>
/// </remarks>
public interface IUserMailHistoryService
{
    /// <summary>One page of the mail sent to the signed-in user, newest first; empty when nobody is signed in.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued for this list.</exception>
    Task<UserMailHistoryPage> GetMineAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>One page of the mail sent to the user with this key, newest first.</summary>
    /// <exception cref="ArgumentException">The user key is empty, or the cursor was not issued for this list.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller is not this user and does not hold <c>users:manage</c>.</exception>
    Task<UserMailHistoryPage> GetForUserAsync(string userKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);
}

/// <summary>One mail in a user's history.</summary>
public sealed record UserMailHistoryItem
{
    /// <summary>The mail's id.</summary>
    public required string Id { get; init; }

    /// <summary>Which kind of mail it was.</summary>
    public required UserMailKind Kind { get; init; }

    /// <summary>The key of the category it was sent in.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>Where it stands.</summary>
    public required UserMailStatus Status { get; init; }

    /// <summary>Why it was skipped or failed, or why the last attempt did not succeed while it waits to be tried again.</summary>
    public string Reason { get; init; }

    /// <summary>The address it was addressed to; null until it is sent, unless it was queued to an address alone.</summary>
    public string Address { get; init; }

    /// <summary>When it was queued, in UTC.</summary>
    public required DateTime QueuedAt { get; init; }

    /// <summary>When it last changed, in UTC.</summary>
    public required DateTime ChangedAt { get; init; }
}

/// <summary>One page of a user's mail history, newest first.</summary>
public sealed record UserMailHistoryPage
{
    /// <summary>The mails on this page, newest first.</summary>
    public UserMailHistoryItem[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}
