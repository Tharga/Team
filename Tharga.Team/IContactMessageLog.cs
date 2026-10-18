using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Keeps the record of every message staff sent to contacts, with the list it was sent to; internal and unchecked, so read the log through <see cref="IContactMessageService"/>, never by injecting this.</summary>
/// <remarks><c>Tharga.Team.MongoDB</c> registers one; register another before it to keep the log elsewhere.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IContactMessageLog
{
    /// <summary>Records a message as sent, with every distinct address it was listed for.</summary>
    Task AddAsync(ContactMessageLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>The recorded message with this id and its addresses, or null.</summary>
    Task<ContactMessageLogEntry> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>One page of recorded messages, newest first, after the cursor a previous page returned; without their addresses.</summary>
    /// <exception cref="ArgumentException">The cursor was not issued by this log.</exception>
    Task<ContactMessageLogPage> ListAsync(string cursor, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>A message to contacts as the log records it when it is sent.</summary>
public sealed record ContactMessageLogEntry
{
    /// <summary>The message's id, which is the id of the batch its mails were queued in.</summary>
    public required string Id { get; init; }

    /// <summary>The contact category's key.</summary>
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

    /// <summary>How many addresses one mail was queued to.</summary>
    public required int RecipientCount { get; init; }

    /// <summary>How many listed addresses had opted out.</summary>
    public int OptedOutCount { get; init; }

    /// <summary>How many listed addresses were left out.</summary>
    public int LeftOutCount { get; init; }

    /// <summary>Every distinct address listed and how it was checked when sent; empty in a page of the log.</summary>
    public ContactAddresseeCheck[] Addressees { get; init; } = [];
}

/// <summary>One page of recorded messages to contacts.</summary>
public sealed record ContactMessageLogPage
{
    /// <summary>The messages on this page, newest first, without their addresses.</summary>
    public ContactMessageLogEntry[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}
