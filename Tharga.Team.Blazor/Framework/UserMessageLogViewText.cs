namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserMessageLogView</c>, the log of messages staff sent to users.</summary>
public static class UserMessageLogViewText
{
    /// <summary>When the reader may not read the log.</summary>
    public static readonly TextKey NotPermitted = new("team.userMessage.log.notPermitted", "You may not read the messages sent to users.");

    /// <summary>When nothing has been sent.</summary>
    public static readonly TextKey Empty = new("team.userMessage.log.empty", "No message has been sent.");

    /// <summary>The button that reads the next page.</summary>
    public static readonly TextKey ShowMore = new("team.userMessage.log.showMore", "Show more");

    /// <summary>Who sent a message; <c>{0}</c> is their name.</summary>
    public static readonly TextKey SentBy = new("team.userMessage.log.sentBy", "Sent by {0}");

    /// <summary>How many a message reached; <c>{0}</c> is the count.</summary>
    public static readonly TextKey Recipients = new("team.userMessage.log.recipients", "{0} recipients");

    /// <summary>Names the category of a message.</summary>
    public static readonly TextKey Category = new("team.userMessage.log.category", "Category");

    /// <summary>Names the selection a message was written to.</summary>
    public static readonly TextKey To = new("team.userMessage.log.to", "To");

    /// <summary>A selection within every team.</summary>
    public static readonly TextKey AllTeams = new("team.userMessage.log.allTeams", "All teams");

    /// <summary>A selection within chosen teams; <c>{0}</c> lists them.</summary>
    public static readonly TextKey Teams = new("team.userMessage.log.teams", "Teams: {0}");

    /// <summary>A selection of everyone in the teams.</summary>
    public static readonly TextKey Everyone = new("team.userMessage.log.everyone", "Everyone in the teams");

    /// <summary>A selection by access level; <c>{0}</c> lists them.</summary>
    public static readonly TextKey AccessLevels = new("team.userMessage.log.accessLevels", "Access levels: {0}");

    /// <summary>A selection by role; <c>{0}</c> lists them.</summary>
    public static readonly TextKey Roles = new("team.userMessage.log.roles", "Roles: {0}");

    /// <summary>A selection of specific users; <c>{0}</c> lists them.</summary>
    public static readonly TextKey Users = new("team.userMessage.log.users", "Users: {0}");

    /// <summary>How many files a message carried; <c>{0}</c> is the count.</summary>
    public static readonly TextKey Attachments = new("team.userMessage.log.attachments", "{0} files attached");

    /// <summary>Names the mails still waiting to be sent.</summary>
    public static readonly TextKey Queued = new("team.userMessage.log.queued", "Waiting");

    /// <summary>Names the mails sent.</summary>
    public static readonly TextKey Sent = new("team.userMessage.log.sent", "Sent");

    /// <summary>Names the mails not sent because the recipient turned the category off.</summary>
    public static readonly TextKey Skipped = new("team.userMessage.log.skipped", "Turned off");

    /// <summary>Names the mails given up.</summary>
    public static readonly TextKey Failed = new("team.userMessage.log.failed", "Failed");

    /// <summary>Heads the list of a message's recipients and what became of each mail.</summary>
    public static readonly TextKey RecipientList = new("team.userMessage.log.recipientList", "Recipients");

    /// <summary>Names the choice of which recipients are listed.</summary>
    public static readonly TextKey StatusFilter = new("team.userMessage.log.statusFilter", "Show recipients");

    /// <summary>The choice that lists every recipient, whatever became of their mail.</summary>
    public static readonly TextKey AnyStatus = new("team.userMessage.log.anyStatus", "All");

    /// <summary>When no recipient matches the choice.</summary>
    public static readonly TextKey NoRecipients = new("team.userMessage.log.noRecipients", "No recipient matches.");

    /// <summary>The button that reads the next page of recipients.</summary>
    public static readonly TextKey MoreRecipients = new("team.userMessage.log.moreRecipients", "Show more recipients");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        NotPermitted, Empty, ShowMore, SentBy, Recipients, Category, To, AllTeams, Teams, Everyone, AccessLevels, Roles, Users,
        Attachments, Queued, Sent, Skipped, Failed, RecipientList, StatusFilter, AnyStatus, NoRecipients, MoreRecipients
    ];
}
