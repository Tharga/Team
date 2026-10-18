namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>ContactMessageLogView</c>, the log of messages staff sent to contacts.</summary>
public static class ContactMessageLogViewText
{
    /// <summary>When the reader may not read the log.</summary>
    public static readonly TextKey NotPermitted = new("team.contactMessage.log.notPermitted", "You may not read the messages sent to contacts.");

    /// <summary>When nothing has been sent.</summary>
    public static readonly TextKey Empty = new("team.contactMessage.log.empty", "No message has been sent to contacts.");

    /// <summary>The button that reads the next page.</summary>
    public static readonly TextKey ShowMore = new("team.contactMessage.log.showMore", "Show more");

    /// <summary>Who sent a message; <c>{0}</c> is their name.</summary>
    public static readonly TextKey SentBy = new("team.contactMessage.log.sentBy", "Sent by {0}");

    /// <summary>How many addresses a message was queued to; <c>{0}</c> is the count.</summary>
    public static readonly TextKey Recipients = new("team.contactMessage.log.recipients", "{0} recipients");

    /// <summary>How many files a message carried; <c>{0}</c> is the count.</summary>
    public static readonly TextKey Attachments = new("team.contactMessage.log.attachments", "{0} files attached");

    /// <summary>Names the mails still waiting to be sent.</summary>
    public static readonly TextKey Queued = new("team.contactMessage.log.queued", "Waiting");

    /// <summary>Names the mails sent.</summary>
    public static readonly TextKey Sent = new("team.contactMessage.log.sent", "Sent");

    /// <summary>Names the addresses that opted out, before the message was sent or after.</summary>
    public static readonly TextKey OptedOut = new("team.contactMessage.log.optedOut", "Opted out");

    /// <summary>Names the mails given up.</summary>
    public static readonly TextKey Failed = new("team.contactMessage.log.failed", "Failed");

    /// <summary>Names the mails the receiving server returned as undeliverable.</summary>
    public static readonly TextKey Bounced = new("team.contactMessage.log.bounced", "Bounced");

    /// <summary>Names the addresses never queued: not a mail address, or left out by the host.</summary>
    public static readonly TextKey LeftOut = new("team.contactMessage.log.leftOut", "Left out");

    /// <summary>Beside an address that opted out of all contact mail.</summary>
    public static readonly TextKey OfEveryCategory = new("team.contactMessage.log.ofEveryCategory", "Of all mail to contacts");

    /// <summary>Beside a listed value that is not a mail address.</summary>
    public static readonly TextKey InvalidAddress = new("team.contactMessage.log.invalidAddress", "Not a mail address");

    /// <summary>Heads the list of a message's addresses and what became of each.</summary>
    public static readonly TextKey RecipientList = new("team.contactMessage.log.recipientList", "Recipients");

    /// <summary>Names the choice of which addresses are listed.</summary>
    public static readonly TextKey StatusFilter = new("team.contactMessage.log.statusFilter", "Show recipients");

    /// <summary>The choice that lists every address, whatever became of it.</summary>
    public static readonly TextKey AnyStatus = new("team.contactMessage.log.anyStatus", "All");

    /// <summary>When no address matches the choice.</summary>
    public static readonly TextKey NoRecipients = new("team.contactMessage.log.noRecipients", "No recipient matches.");

    /// <summary>The button that reads the next page of addresses.</summary>
    public static readonly TextKey MoreRecipients = new("team.contactMessage.log.moreRecipients", "Show more recipients");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        NotPermitted, Empty, ShowMore, SentBy, Recipients, Attachments, Queued, Sent, OptedOut, Failed, Bounced, LeftOut, OfEveryCategory, InvalidAddress,
        RecipientList, StatusFilter, AnyStatus, NoRecipients, MoreRecipients
    ];
}
