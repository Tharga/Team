namespace Tharga.Team;

/// <summary>One mail to many users, handed to <see cref="IUserMailQueue.QueueBatchAsync"/>, whose content and files are stored once however many it goes to.</summary>
public sealed record UserMailBatch
{
    /// <summary>The keys of the users the mail is for; each is queued once, however often it is named.</summary>
    public required string[] RecipientUserKeys { get; init; }

    /// <summary>Addresses the mail goes to alone, with no user key, each queued once; for contact mail, which the contact message service queues and <see cref="IUserMailQueue"/> refuses.</summary>
    public UserMailBatchAddress[] RecipientAddresses { get; init; } = [];

    /// <summary>The key of a registered category; an unknown key is refused when the batch is queued.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>The body as HTML; sanitized when queued, and the text body is derived from it when none is given.</summary>
    public string BodyHtml { get; init; }

    /// <summary>The body as plain text; required when there is no HTML body.</summary>
    public string Body { get; init; }

    /// <summary>Files sent with every mail of the batch, stored once and within the configured limits.</summary>
    public UserMailAttachment[] Attachments { get; init; } = [];

    /// <summary>Which kind of mail every mail of the batch is, shown in each recipient's history; default <see cref="UserMailKind.Message"/>.</summary>
    public UserMailKind Kind { get; init; } = UserMailKind.Message;

    /// <summary>The key of the team every mail of the batch is about, so the mail log can be filtered by organisation; null when it belongs to none or to several.</summary>
    public string TeamKey { get; init; }
}

/// <summary>An address one mail of a batch goes to alone, and the name it is addressed to.</summary>
/// <param name="Address">The address, trimmed and in lower case.</param>
/// <param name="Name">The name the mail is addressed to, or null.</param>
public sealed record UserMailBatchAddress(string Address, string Name = null);

/// <summary>The outcome of <see cref="IUserMailQueue.QueueBatchAsync"/>.</summary>
/// <param name="Id">The id of the batch, which <see cref="IUserMailQueue.GetBatchAsync"/> reads its outcomes by.</param>
/// <param name="Recipients">How many mails were queued, one per distinct recipient.</param>
public sealed record UserMailBatchQueued(string Id, int Recipients);

/// <summary>How many mails of a batch stand where.</summary>
public sealed record UserMailOutcomes
{
    /// <summary>Waiting to be sent, or to be tried again.</summary>
    public int Queued { get; init; }

    /// <summary>Accepted by the mail server and not reported bounced.</summary>
    public int Sent { get; init; }

    /// <summary>Not sent, because the recipient does not receive the category.</summary>
    public int Skipped { get; init; }

    /// <summary>Not sent and given up.</summary>
    public int Failed { get; init; }

    /// <summary>Accepted by the mail server, then returned by the receiving one as undeliverable; counted here instead of in <see cref="Sent"/>.</summary>
    public int Bounced { get; init; }
}

/// <summary>Why the mail to one recipient of a batch was given up.</summary>
public sealed record UserMailFailure
{
    /// <summary>The key of the user the mail was for.</summary>
    public required string UserKey { get; init; }

    /// <summary>Why it was given up.</summary>
    public required string Reason { get; init; }
}

/// <summary>What became of a queued batch.</summary>
public sealed record UserMailBatchDelivery
{
    /// <summary>The most failures a delivery lists.</summary>
    public const int MaxFailuresListed = 100;

    /// <summary>The batch's id.</summary>
    public required string Id { get; init; }

    /// <summary>How many mails were queued for it.</summary>
    public int Recipients { get; init; }

    /// <summary>How many of them stand where.</summary>
    public required UserMailOutcomes Outcomes { get; init; }

    /// <summary>The mails given up, each with why, at most <see cref="MaxFailuresListed"/>.</summary>
    public UserMailFailure[] Failures { get; init; } = [];

    /// <summary>When the last mail of the batch was sent, skipped or given up, in UTC; null while any waits.</summary>
    public DateTime? CompletedAt { get; init; }
}
