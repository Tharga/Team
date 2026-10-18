using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Keeps the record of every message staff sent; internal and unchecked, so read the log through <see cref="IUserMessageService"/>, never by injecting this.</summary>
/// <remarks><c>Tharga.Team.MongoDB</c> registers one; register another before it to keep the log elsewhere.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserMessageLog
{
    /// <summary>Records a message as sent.</summary>
    Task AddAsync(UserMessageLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>The recorded message with this id, or null.</summary>
    Task<UserMessageLogEntry> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>One page of recorded messages, newest first, after the cursor a previous page returned.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued by this log.</exception>
    Task<UserMessageLogPage> ListAsync(string cursor, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>A message as the log records it when it is sent.</summary>
public sealed record UserMessageLogEntry
{
    /// <summary>The message's id, which is the id of the batch its mails were queued in.</summary>
    public required string Id { get; init; }

    /// <summary>The selection as it was chosen.</summary>
    public required UserMessageRecipients Recipients { get; init; }

    /// <summary>The category's key.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>How many files it carried.</summary>
    public int AttachmentCount { get; init; }

    /// <summary>The key of the user who sent it, or null.</summary>
    public string SentByUserKey { get; init; }

    /// <summary>The name of whoever sent it.</summary>
    public string SentByName { get; init; }

    /// <summary>When it was sent, in UTC.</summary>
    public required DateTime SentAt { get; init; }

    /// <summary>How many users it reached when it was sent.</summary>
    public required int RecipientCount { get; init; }
}

/// <summary>One page of recorded messages.</summary>
public sealed record UserMessageLogPage
{
    /// <summary>The messages on this page, newest first.</summary>
    public UserMessageLogEntry[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}
