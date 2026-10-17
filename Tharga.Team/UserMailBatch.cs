namespace Tharga.Team;

/// <summary>One mail to many users, handed to <see cref="IUserMailQueue.QueueBatchAsync"/>, whose content and files are stored once however many it goes to.</summary>
public sealed record UserMailBatch
{
    /// <summary>The keys of the users the mail is for; each is queued once, however often it is named.</summary>
    public required string[] RecipientUserKeys { get; init; }

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
}

/// <summary>The outcome of <see cref="IUserMailQueue.QueueBatchAsync"/>.</summary>
/// <param name="Id">The id of the batch, which <see cref="IUserMailQueue.GetBatchAsync"/> reads its outcomes by.</param>
/// <param name="Recipients">How many mails were queued, one per distinct recipient.</param>
public sealed record UserMailBatchQueued(string Id, int Recipients);

/// <summary>How many mails of a batch stand where.</summary>
public sealed record UserMailOutcomes
{
    /// <summary>Waiting to be sent, or to be tried again.</summary>
    public int Queued { get; init; }

    /// <summary>Accepted by the mail server.</summary>
    public int Sent { get; init; }

    /// <summary>Not sent, because the recipient does not receive the category.</summary>
    public int Skipped { get; init; }

    /// <summary>Not sent and given up.</summary>
    public int Failed { get; init; }
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
