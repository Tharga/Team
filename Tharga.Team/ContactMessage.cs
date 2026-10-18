namespace Tharga.Team;

/// <summary>One person the host lists as an addressee of a message to contacts: an address, and what the host knows of them.</summary>
/// <remarks>
/// <b>The host chooses who is on the list</b>, from its own records and with its own filters; the toolkit only mails them,
/// once per address, leaving out whoever opted out. <see cref="Reference"/> comes back with every outcome, so the host can
/// record what was sent on its own record.
/// </remarks>
public sealed record ContactAddressee
{
    /// <summary>The address to mail; compared trimmed and ignoring case, so two rows naming the same address are one mail.</summary>
    public required string Address { get; init; }

    /// <summary>The name the mail is addressed to, or null.</summary>
    public string Name { get; init; }

    /// <summary>The host's own reference for the row, such as the key of its record, returned with the outcome; null for none.</summary>
    public string Reference { get; init; }

    /// <summary>A short line shown beside the addressee, such as the organisation they belong to; null for none.</summary>
    public string Detail { get; init; }

    /// <summary>Why the host leaves this row out although it lists it, shown as it is; null to mail them.</summary>
    /// <remarks>An address is mailed when any row naming it has no reason; a row left out never makes another row's address go unmailed.</remarks>
    public string ExcludedReason { get; init; }
}

/// <summary>A message to write to contacts, as handed to <see cref="IContactMessageService.SendAsync"/>.</summary>
public sealed record ContactMessage
{
    /// <summary>Who it goes to, as the host listed them; each address is mailed once.</summary>
    public required ContactAddressee[] Addressees { get; init; }

    /// <summary>The key of a contact category; any other is refused.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>The body as HTML; sanitized on the server, and the text body is derived from it when none is given.</summary>
    public string BodyHtml { get; init; }

    /// <summary>The body as plain text; required when there is no HTML body.</summary>
    public string Body { get; init; }

    /// <summary>Files sent with the message, stored once however many it reaches.</summary>
    public UserMailAttachment[] Attachments { get; init; } = [];
}

/// <summary>Whether a listed address will be mailed.</summary>
public enum ContactAddresseeStatus
{
    /// <summary>It will be mailed.</summary>
    Reached,

    /// <summary>It opted out of the category, or of all contact mail, and will not be mailed.</summary>
    OptedOut,

    /// <summary>It is left out: it is not a mail address, or the host gave a reason for every row naming it.</summary>
    LeftOut
}

/// <summary>One distinct address of a list, and whether it will be mailed.</summary>
public sealed record ContactAddresseeCheck
{
    /// <summary>The address trimmed and in lower case; as listed, trimmed, when it is not a mail address.</summary>
    public required string Address { get; init; }

    /// <summary>The name of the first row naming the address that has one.</summary>
    public string Name { get; init; }

    /// <summary>The detail line of the first row naming the address that has one.</summary>
    public string Detail { get; init; }

    /// <summary>The host's references of every row the address stands for: those mailed, or when none is, every row naming it.</summary>
    public string[] References { get; init; } = [];

    /// <summary>Whether it will be mailed.</summary>
    public required ContactAddresseeStatus Status { get; init; }

    /// <summary>For <see cref="ContactAddresseeStatus.OptedOut"/>: of all contact mail rather than this category alone.</summary>
    public bool OptedOutOfAll { get; init; }

    /// <summary>For <see cref="ContactAddresseeStatus.LeftOut"/>: it is not a mail address.</summary>
    public bool InvalidAddress { get; init; }

    /// <summary>For <see cref="ContactAddresseeStatus.LeftOut"/>: the host's reason, as it gave it.</summary>
    public string ExcludedReason { get; init; }
}

/// <summary>A list checked before sending: each distinct address in the order first listed, with the counts.</summary>
public sealed record ContactAddresseeList
{
    /// <summary>Each distinct address and whether it will be mailed.</summary>
    public ContactAddresseeCheck[] Items { get; init; } = [];

    /// <summary>How many addresses will be mailed.</summary>
    public int Reached { get; init; }

    /// <summary>How many opted out.</summary>
    public int OptedOut { get; init; }

    /// <summary>How many are left out, not being addresses or for the host's reason.</summary>
    public int LeftOut { get; init; }
}

/// <summary>What a message to contacts may be written in and the limits it is held to.</summary>
public sealed record ContactMessageChoices
{
    /// <summary>The contact categories, in registration order.</summary>
    public ContactMailCategory[] Categories { get; init; } = [];

    /// <summary>The limits on its body and files.</summary>
    public required UserMessageLimits Limits { get; init; }

    /// <summary>The most rows one list may hold.</summary>
    public int MaxAddressees { get; init; }
}

/// <summary>Where the mail to one address of a message to contacts stands.</summary>
public enum ContactRecipientStatus
{
    /// <summary>Waiting to be sent, or to be tried again.</summary>
    Queued,

    /// <summary>Accepted by the mail server.</summary>
    Sent,

    /// <summary>Not sent, because the address opted out, before the message was sent or after.</summary>
    OptedOut,

    /// <summary>Not sent and given up, for the reason recorded.</summary>
    Failed,

    /// <summary>Accepted, then returned by the receiving server as undeliverable.</summary>
    Bounced,

    /// <summary>Never queued: not a mail address, or left out for the host's reason.</summary>
    LeftOut
}

/// <summary>One address of a message to contacts and what became of the mail to it.</summary>
public sealed record ContactMessageRecipient
{
    /// <summary>The address, trimmed and in lower case; as listed when it is not a mail address.</summary>
    public required string Address { get; init; }

    /// <summary>The name it was addressed to, or null.</summary>
    public string Name { get; init; }

    /// <summary>The detail line the host gave, or null.</summary>
    public string Detail { get; init; }

    /// <summary>The host's references of the rows the address stands for.</summary>
    public string[] References { get; init; } = [];

    /// <summary>Where it stands.</summary>
    public required ContactRecipientStatus Status { get; init; }

    /// <summary>For <see cref="ContactRecipientStatus.OptedOut"/>: of all contact mail rather than this category alone.</summary>
    public bool OptedOutOfAll { get; init; }

    /// <summary>For <see cref="ContactRecipientStatus.LeftOut"/>: it is not a mail address.</summary>
    public bool InvalidAddress { get; init; }

    /// <summary>The host's reason for leaving it out, why it failed or bounced, or why the last attempt did not succeed while it waits to be tried again.</summary>
    public string Reason { get; init; }

    /// <summary>When it last changed, in UTC; when the message was sent for an address never queued.</summary>
    public required DateTime ChangedAt { get; init; }

    /// <summary>The <c>Message-ID</c> it was sent with; null until it is sent.</summary>
    public string MessageId { get; init; }
}

/// <summary>A message to contacts as the log keeps it: what it was, who sent it in which category, and what became of each mail.</summary>
public sealed record SentContactMessage
{
    /// <summary>The message's id.</summary>
    public required string Id { get; init; }

    /// <summary>The contact category's key.</summary>
    public required string Category { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>How many files it carried.</summary>
    public int AttachmentCount { get; init; }

    /// <summary>The key of the user who sent it, or null when the sender had no user record.</summary>
    public string SentByUserKey { get; init; }

    /// <summary>The name of whoever sent it, as it was when they sent it.</summary>
    public string SentByName { get; init; }

    /// <summary>When it was sent, in UTC.</summary>
    public required DateTime SentAt { get; init; }

    /// <summary>How many addresses one mail was queued to.</summary>
    public required int RecipientCount { get; init; }

    /// <summary>How many listed addresses had opted out when it was sent, and were not queued.</summary>
    public int OptedOutCount { get; init; }

    /// <summary>How many listed addresses were left out when it was sent: not addresses, or for the host's reason.</summary>
    public int LeftOutCount { get; init; }

    /// <summary>How many of its queued mails stand where; an address that opted out after it was queued is counted as skipped.</summary>
    public required UserMailOutcomes Outcomes { get; init; }

    /// <summary>Every distinct address listed and where it stood when the message was sent, with the host's references; filled only by <see cref="IContactMessageService.SendAsync"/>.</summary>
    public ContactMessageRecipient[] Recipients { get; init; } = [];
}

/// <summary>One page of the log of messages to contacts, newest first.</summary>
public sealed record SentContactMessagePage
{
    /// <summary>The messages on this page.</summary>
    public SentContactMessage[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}

/// <summary>One page of a message's addresses: those queued in the order they were, then those never queued.</summary>
public sealed record ContactMessageRecipientPage
{
    /// <summary>The addresses on this page.</summary>
    public ContactMessageRecipient[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}
