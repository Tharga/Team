namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserMailLogView</c> — every mail the application sent or tried to send. The names of kinds and results are <see cref="UserMailHistoryViewText"/>'s.</summary>
public static class UserMailLogViewText
{
    /// <summary>The section's title.</summary>
    public static readonly TextKey Title = new("team.userMail.log.title", "Mail log");

    /// <summary>One sentence above the list.</summary>
    public static readonly TextKey Explanation = new("team.userMail.log.explanation",
        "Every mail the application sent or tried to send to users and contacts, newest first. The mail itself is not shown.");

    /// <summary>When nothing matches.</summary>
    public static readonly TextKey Empty = new("team.userMail.log.empty", "No mail matches.");

    /// <summary>The button that reads the next page.</summary>
    public static readonly TextKey ShowMore = new("team.userMail.log.showMore", "Show more");

    /// <summary>When the reader may not see the log.</summary>
    public static readonly TextKey NotPermitted = new("team.userMail.log.notPermitted", "You cannot see the mail log.");

    public static readonly TextKey Search = new("team.userMail.log.search", "Search");
    public static readonly TextKey Clear = new("team.userMail.log.clear", "Clear");

    /// <summary>The filters. The kind, result and category ones say what they are for before one is chosen.</summary>
    public static readonly TextKey FilterKind = new("team.userMail.log.filter.kind", "Any kind");
    public static readonly TextKey FilterStatus = new("team.userMail.log.filter.status", "Any result");
    public static readonly TextKey FilterCategory = new("team.userMail.log.filter.category", "Any category");
    public static readonly TextKey FilterFrom = new("team.userMail.log.filter.from", "From");
    public static readonly TextKey FilterTo = new("team.userMail.log.filter.to", "To");
    public static readonly TextKey FilterRecipient = new("team.userMail.log.filter.recipient", "User key or address");
    public static readonly TextKey FilterTeam = new("team.userMail.log.filter.team", "Team key");

    /// <summary>The columns.</summary>
    public static readonly TextKey ColumnWhen = new("team.userMail.log.column.when", "When");
    public static readonly TextKey ColumnRecipient = new("team.userMail.log.column.recipient", "To");
    public static readonly TextKey ColumnKind = new("team.userMail.log.column.kind", "Kind");
    public static readonly TextKey ColumnCategory = new("team.userMail.log.column.category", "Category");
    public static readonly TextKey ColumnSubject = new("team.userMail.log.column.subject", "Subject");
    public static readonly TextKey ColumnResult = new("team.userMail.log.column.result", "Result");
    public static readonly TextKey ColumnTeam = new("team.userMail.log.column.team", "Team");

    /// <summary>The row action that shows all the mail sent to one user; <c>{0}</c> identifies the user.</summary>
    public static readonly TextKey UserMail = new("team.userMail.log.userMail", "Mail sent to {0}");

    /// <summary>The title of that dialog; <c>{0}</c> identifies the user.</summary>
    public static readonly TextKey UserMailTitle = new("team.userMail.log.userMailTitle", "Mail sent — {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, Explanation, Empty, ShowMore, NotPermitted, Search, Clear, FilterKind, FilterStatus, FilterCategory, FilterFrom,
        FilterTo, FilterRecipient, FilterTeam, ColumnWhen, ColumnRecipient, ColumnKind, ColumnCategory, ColumnSubject,
        ColumnResult, ColumnTeam, UserMail, UserMailTitle
    ];
}
