namespace Tharga.Team;

/// <summary>Who a message goes to: the teams it applies within, and within them the users chosen by key, role, access level or all of them, with duplicates removed.</summary>
/// <remarks>
/// <b>Only active members count:</b> a member who has accepted, is not suspended, has a user record that is not
/// disabled, in a team that is not deleted. Invited and rejected members, suspended members, disabled accounts and
/// deleted teams are left out.
/// </remarks>
public sealed record UserMessageRecipients
{
    /// <summary>The teams the selection applies within; ignored when <see cref="AllTeams"/> is set.</summary>
    public string[] TeamKeys { get; init; } = [];

    /// <summary>Whether the selection applies within every team.</summary>
    public bool AllTeams { get; init; }

    /// <summary>Users chosen by key, reached where they are an active member of one of the teams.</summary>
    public string[] UserKeys { get; init; } = [];

    /// <summary>Everyone in the teams holding any of these tenant roles.</summary>
    public string[] Roles { get; init; } = [];

    /// <summary>Everyone in the teams at any of these access levels.</summary>
    public AccessLevel[] AccessLevels { get; init; } = [];

    /// <summary>Whether everyone in the teams is reached.</summary>
    public bool EveryMember { get; init; }
}

/// <summary>A message to write to chosen users, as handed to <see cref="IUserMessageService.SendAsync"/>.</summary>
public sealed record UserMessage
{
    /// <summary>Who it goes to.</summary>
    public required UserMessageRecipients Recipients { get; init; }

    /// <summary>The key of a category staff may write in; any other is refused.</summary>
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

/// <summary>How many people a selection reaches before it is sent, never who.</summary>
public sealed record UserMessagePreview
{
    /// <summary>How many distinct users the selection reaches.</summary>
    public required int Recipients { get; init; }

    /// <summary>How many of them turned the category off and will not receive it; always zero for a required category.</summary>
    public required int LeftOut { get; init; }
}

/// <summary>What a message may be written to and in: the teams, the roles and access levels within them, the categories and the limits.</summary>
/// <remarks>Users are not listed, because a product may have tens of thousands; find them with <see cref="IUserMessageService.SearchUsersAsync"/>.</remarks>
public sealed record UserMessageChoices
{
    /// <summary>Every team that is not deleted.</summary>
    public UserMessageTeam[] Teams { get; init; } = [];

    /// <summary>The tenant roles members hold.</summary>
    public string[] Roles { get; init; } = [];

    /// <summary>The access levels members are at.</summary>
    public AccessLevel[] AccessLevels { get; init; } = [];

    /// <summary>The categories staff may write in, in registration order.</summary>
    public UserMailCategory[] Categories { get; init; } = [];

    /// <summary>The limits a message is held to.</summary>
    public required UserMessageLimits Limits { get; init; }
}

/// <summary>A team a message may be written within.</summary>
public sealed record UserMessageTeam
{
    /// <summary>The team's key.</summary>
    public required string Key { get; init; }

    /// <summary>The team's name.</summary>
    public string Name { get; init; }
}

/// <summary>A user a message may be written to.</summary>
public sealed record UserMessageCandidate
{
    /// <summary>The user's key.</summary>
    public required string UserKey { get; init; }

    /// <summary>The user's name as the first of their memberships has it, or null.</summary>
    public string Name { get; init; }

    /// <summary>The address on the user's record, or null.</summary>
    public string EMail { get; init; }

    /// <summary>The teams the user is an active member of, among those searched.</summary>
    public string[] TeamKeys { get; init; } = [];
}

/// <summary>A search for users a message may be written to, as handed to <see cref="IUserMessageService.SearchUsersAsync"/>.</summary>
public sealed record UserMessageUserSearch
{
    /// <summary>The most users a search returns.</summary>
    public const int MaxLimit = 50;

    /// <summary>What the name or the address holds, ignoring case; empty finds nobody.</summary>
    public string Text { get; init; }

    /// <summary>The teams to search among; ignored when <see cref="AllTeams"/> is set, and none finds nobody.</summary>
    public string[] TeamKeys { get; init; } = [];

    /// <summary>Whether to search among every team.</summary>
    public bool AllTeams { get; init; }

    /// <summary>The most users returned, between 1 and <see cref="MaxLimit"/>; default 20.</summary>
    public int Limit { get; init; } = 20;
}

/// <summary>The limits a message is held to.</summary>
public sealed record UserMessageLimits
{
    /// <summary>The most files one message may carry.</summary>
    public required int MaxAttachmentCount { get; init; }

    /// <summary>The most all files on one message may add up to, in bytes.</summary>
    public required long MaxAttachmentBytes { get; init; }

    /// <summary>The most characters the HTML and text bodies may hold together.</summary>
    public required int MaxBodyLength { get; init; }
}

/// <summary>A message as the sent log keeps it: what it was, who sent it to which selection, and what became of each mail.</summary>
public sealed record SentUserMessage
{
    /// <summary>The message's id.</summary>
    public required string Id { get; init; }

    /// <summary>The selection as it was chosen.</summary>
    public required UserMessageRecipients Recipients { get; init; }

    /// <summary>The category's key.</summary>
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

    /// <summary>How many users the selection reached when it was sent, one mail queued for each.</summary>
    public required int RecipientCount { get; init; }

    /// <summary>How many of its mails stand where.</summary>
    public required UserMailOutcomes Outcomes { get; init; }

    /// <summary>The mails given up, each with why; filled only when one message is read.</summary>
    public UserMessageFailure[] Failures { get; init; } = [];

    /// <summary>The users the selection named by key, with their names as their records have them now; filled only when one message is read.</summary>
    public UserMessageCandidate[] ChosenUsers { get; init; } = [];
}

/// <summary>Why the mail to one recipient of a message was given up.</summary>
public sealed record UserMessageFailure
{
    /// <summary>The key of the user the mail was for.</summary>
    public required string UserKey { get; init; }

    /// <summary>The user's name, or null when they have none or no longer exist.</summary>
    public string Name { get; init; }

    /// <summary>Why it was given up.</summary>
    public required string Reason { get; init; }
}

/// <summary>One recipient of a sent message and what became of the mail to them.</summary>
public sealed record UserMessageRecipient
{
    /// <summary>The key of the user the mail was for.</summary>
    public required string UserKey { get; init; }

    /// <summary>The user's name, or null when they have none or no longer exist.</summary>
    public string Name { get; init; }

    /// <summary>The address the mail went to, as the user record had it when it was sent; null until then.</summary>
    public string Address { get; init; }

    /// <summary>Where the mail stands.</summary>
    public required UserMailStatus Status { get; init; }

    /// <summary>Why it was skipped or failed, or why the last attempt did not succeed while it waits to be tried again.</summary>
    public string Reason { get; init; }

    /// <summary>When it last changed, in UTC.</summary>
    public required DateTime ChangedAt { get; init; }

    /// <summary>The <c>Message-ID</c> it was sent with; null until it is sent.</summary>
    public string MessageId { get; init; }
}

/// <summary>One page of a sent message's recipients.</summary>
public sealed record UserMessageRecipientPage
{
    /// <summary>The recipients on this page, in the order they were queued.</summary>
    public UserMessageRecipient[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}

/// <summary>One page of the sent log, newest first.</summary>
public sealed record SentUserMessagePage
{
    /// <summary>The messages on this page.</summary>
    public SentUserMessage[] Items { get; init; } = [];

    /// <summary>The cursor of the next page, or null when this is the last.</summary>
    public string NextCursor { get; init; }
}
