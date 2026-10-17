namespace Tharga.Team;

/// <summary>A mail to one user, in one category, as it is handed to <see cref="IUserMailQueue"/>.</summary>
/// <remarks>
/// The recipient is named by user key, never by address: the address is read from the user record when the mail is
/// sent, as is whether the user still receives the category, so a change made after queuing is honoured.
/// </remarks>
public sealed record UserMail
{
    /// <summary>The key of the user the mail is for.</summary>
    public required string RecipientUserKey { get; init; }

    /// <summary>The key of a registered category; an unknown key is refused when the mail is queued.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>The body as HTML; sanitized when queued, and the text body is derived from it when none is given.</summary>
    public string BodyHtml { get; init; }

    /// <summary>The body as plain text; required when there is no HTML body.</summary>
    public string Body { get; init; }

    /// <summary>Files sent with the mail, held in the queue until it is sent and within the configured limits.</summary>
    public UserMailAttachment[] Attachments { get; init; } = [];

    /// <summary>Where replies go instead of the configured reply address; null uses the configured one.</summary>
    public string ReplyTo { get; init; }

    /// <summary>A key that makes queuing idempotent: a second mail with the same key is not queued again.</summary>
    public string DeduplicationKey { get; init; }
}

/// <summary>A file sent with a <see cref="UserMail"/>.</summary>
public sealed record UserMailAttachment
{
    /// <summary>The name the recipient sees; it is sanitized when the mail is sent.</summary>
    public required string FileName { get; init; }

    /// <summary>The declared content type; one that does not parse is sent as <c>application/octet-stream</c>.</summary>
    public required string ContentType { get; init; }

    /// <summary>The file's bytes; null once the mail has been sent or given up, when only the description is kept.</summary>
    public byte[] Content { get; init; }

    /// <summary>The size in bytes, kept when the content is no longer.</summary>
    public long Size { get; init; }

    /// <summary>The Content-ID the HTML body names the file by as <c>cid:</c>, without angle brackets; null for a plain attachment.</summary>
    public string ContentId { get; init; }
}
