namespace Tharga.Team.Email.Users;

/// <summary>What audit entries about contact mail are recorded under, for a host reading or routing them; none names an address.</summary>
public static class ContactMessageAudit
{
    /// <summary>The feature every entry about contact mail carries.</summary>
    public const string Feature = "contacts";

    /// <summary>The action of a message to contacts sent, or refused.</summary>
    public const string SendAction = "send";

    /// <summary>The action of a page of the sent log read.</summary>
    public const string LogAction = "log";

    /// <summary>The action of one sent message read with its outcomes.</summary>
    public const string OpenAction = "open";

    /// <summary>The action of a page of one sent message's recipients read.</summary>
    public const string RecipientsAction = "recipients";

    /// <summary>The action of an address opted out from an unsubscribe link, on its page or by a one-click request.</summary>
    public const string UnsubscribeAction = "unsubscribe";

    /// <summary>The action of staff opting an address out.</summary>
    public const string SuppressAction = "suppress";

    /// <summary>Metadata naming the message.</summary>
    public const string MessageId = "contacts.id";

    /// <summary>Metadata naming the category, or <see cref="EveryCategory"/> for an opt-out of all contact mail.</summary>
    public const string Category = "contacts.category";

    /// <summary>Metadata holding the subject.</summary>
    public const string Subject = "contacts.subject";

    /// <summary>Metadata holding how many addresses a message was queued to.</summary>
    public const string Recipients = "contacts.recipients";

    /// <summary>Metadata holding how many addresses a message left out because they had opted out.</summary>
    public const string OptedOut = "contacts.optedOut";

    /// <summary>Metadata holding how many addresses a message left out for another reason: not an address, or the host's own.</summary>
    public const string LeftOut = "contacts.leftOut";

    /// <summary>Metadata holding how many files a message carried.</summary>
    public const string Attachments = "contacts.attachments";

    /// <summary>Metadata holding how many items a page held.</summary>
    public const string Count = "contacts.count";

    /// <summary>Metadata naming the status a page of recipients was limited to.</summary>
    public const string Status = "contacts.status";

    /// <summary>Metadata holding how many mails of a message still wait.</summary>
    public const string Queued = "contacts.queued";

    /// <summary>Metadata holding how many mails of a message were sent.</summary>
    public const string Sent = "contacts.sent";

    /// <summary>Metadata holding how many mails of a message were not sent because the address opted out after it was queued.</summary>
    public const string Skipped = "contacts.skipped";

    /// <summary>Metadata holding how many mails of a message were given up.</summary>
    public const string Failed = "contacts.failed";

    /// <summary>Metadata holding how many mails of a message the receiving server returned as undeliverable.</summary>
    public const string Bounced = "contacts.bounced";

    /// <summary>Metadata saying how an opt-out was asked for: <c>Link</c>, <c>OneClick</c> or <c>Staff</c>.</summary>
    public const string Source = "contacts.source";

    /// <summary>The value of <see cref="Category"/> for an opt-out of all contact mail.</summary>
    public const string EveryCategory = "all";
}
