namespace Tharga.Team.Email;

/// <summary>Applies a delivery status notification — a bounce — about a mail that something other than support sent.</summary>
/// <remarks>
/// <b>Bounces arrive where the mail was sent from</b>, so they are only seen when that address is a mailbox the toolkit
/// reads. The support mail poller reads its mailboxes, recognises a bounce, and applies it to the support answer it
/// names; a bounce that names no support answer is handed to every registered handler in turn until one recognises
/// it. Mail to users registers one, so user mail sent from a support mailbox's address is marked bounced.
/// <para>
/// A handler is called again for the same report whenever a mailbox is read again, by another instance or after a
/// restart, so it must be idempotent. The reason often quotes the recipient's address: keep it where it belongs and
/// do not log it.
/// </para>
/// </remarks>
public interface IMailBounceHandler
{
    /// <summary>True when the bounce names a mail this handler knows, whether or not anything changed; false lets the next handler try.</summary>
    Task<bool> HandleAsync(MailBounce bounce, CancellationToken cancellationToken = default);
}

/// <summary>What a bounce reports: the mail it returns and why.</summary>
public sealed record MailBounce
{
    /// <summary>The <c>Message-ID</c> of the mail that bounced, without angle brackets.</summary>
    public required string OriginalMessageId { get; init; }

    /// <summary>The receiving server's own words for why, or null when the report gave none.</summary>
    public string Reason { get; init; }

    /// <summary>When the report was read, in UTC.</summary>
    public required DateTime ReceivedAt { get; init; }
}
