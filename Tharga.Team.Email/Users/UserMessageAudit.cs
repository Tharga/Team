namespace Tharga.Team.Email.Users;

/// <summary>What audit entries about messages to users are recorded under, for a host reading or routing them.</summary>
public static class UserMessageAudit
{
    /// <summary>The feature every entry about messages carries.</summary>
    public const string Feature = "messages";

    /// <summary>The action of a message sent, or refused.</summary>
    public const string SendAction = "send";

    /// <summary>The action of a page of the sent log read.</summary>
    public const string LogAction = "log";

    /// <summary>The action of one sent message read with its outcomes.</summary>
    public const string OpenAction = "open";

    /// <summary>The action of a page of one sent message's recipients read.</summary>
    public const string RecipientsAction = "recipients";

    /// <summary>Metadata naming the status a page of recipients was limited to.</summary>
    public const string Status = "messages.status";

    /// <summary>Metadata naming the message.</summary>
    public const string MessageId = "messages.id";

    /// <summary>Metadata naming the category.</summary>
    public const string Category = "messages.category";

    /// <summary>Metadata holding the subject.</summary>
    public const string Subject = "messages.subject";

    /// <summary>Metadata holding how many users a message reached.</summary>
    public const string Recipients = "messages.recipients";

    /// <summary>Metadata holding how many files a message carried.</summary>
    public const string Attachments = "messages.attachments";

    /// <summary>Metadata holding how many teams a message was written within, or <c>all</c>.</summary>
    public const string Teams = "messages.teams";

    /// <summary>Metadata holding how many messages a page of the log held.</summary>
    public const string Count = "messages.count";

    /// <summary>Metadata holding how many mails of a message were sent.</summary>
    public const string Sent = "messages.sent";

    /// <summary>Metadata holding how many mails of a message were skipped because the recipient turned the category off.</summary>
    public const string Skipped = "messages.skipped";

    /// <summary>Metadata holding how many mails of a message were given up.</summary>
    public const string Failed = "messages.failed";

    /// <summary>Metadata holding how many mails of a message still wait.</summary>
    public const string Queued = "messages.queued";

    /// <summary>Metadata holding how many mails of a message the receiving server returned as undeliverable.</summary>
    public const string Bounced = "messages.bounced";

    /// <summary>The value of <see cref="Teams"/> for a message written within every team.</summary>
    public const string AllTeams = "all";
}
