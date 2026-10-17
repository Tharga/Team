namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserMailHistoryView</c> — the mail sent to one user.</summary>
public static class UserMailHistoryViewText
{
    /// <summary>The section's title.</summary>
    public static readonly TextKey Title = new("team.userMail.history.title", "Mail sent");

    /// <summary>One sentence above the list.</summary>
    public static readonly TextKey Explanation = new("team.userMail.history.explanation", "The mail sent to this account, newest first.");

    /// <summary>When nothing has been sent.</summary>
    public static readonly TextKey Empty = new("team.userMail.history.empty", "No mail has been sent.");

    /// <summary>The button that reads the next page.</summary>
    public static readonly TextKey ShowMore = new("team.userMail.history.showMore", "Show more");

    /// <summary>When the reader may not see this user's mail.</summary>
    public static readonly TextKey NotPermitted = new("team.userMail.history.notPermitted", "You cannot see the mail sent to this user.");

    /// <summary>When nobody is signed in and no user was named.</summary>
    public static readonly TextKey NoUser = new("team.userMail.history.noUser", "Sign in to see the mail sent to you.");

    /// <summary>The address a mail went to; <c>{0}</c> is the address.</summary>
    public static readonly TextKey To = new("team.userMail.history.to", "To {0}");

    /// <summary>A mail server code sent on its own.</summary>
    public static readonly TextKey KindMail = new("team.userMail.history.kind.mail", "Mail");

    /// <summary>A message staff wrote.</summary>
    public static readonly TextKey KindMessage = new("team.userMail.history.kind.message", "Message");

    /// <summary>An automatic notice or summary.</summary>
    public static readonly TextKey KindNotice = new("team.userMail.history.kind.notice", "Notice");

    /// <summary>An invitation to a team.</summary>
    public static readonly TextKey KindInvitation = new("team.userMail.history.kind.invitation", "Invitation");

    /// <summary>A mail waiting to be sent, or to be tried again.</summary>
    public static readonly TextKey StatusQueued = new("team.userMail.history.status.queued", "Waiting");

    /// <summary>A mail the mail server accepted.</summary>
    public static readonly TextKey StatusSent = new("team.userMail.history.status.sent", "Sent");

    /// <summary>A mail not sent because its category was turned off.</summary>
    public static readonly TextKey StatusSkipped = new("team.userMail.history.status.skipped", "Turned off");

    /// <summary>A mail that could not be sent.</summary>
    public static readonly TextKey StatusFailed = new("team.userMail.history.status.failed", "Not sent");

    /// <summary>A mail the receiving server returned as undeliverable.</summary>
    public static readonly TextKey StatusBounced = new("team.userMail.history.status.bounced", "Not delivered");

    /// <summary>The name of a kind of mail.</summary>
    public static TextKey For(UserMailKind kind) => kind switch
    {
        UserMailKind.Message => KindMessage,
        UserMailKind.Notice => KindNotice,
        UserMailKind.Invitation => KindInvitation,
        _ => KindMail
    };

    /// <summary>The name of where a mail stands.</summary>
    public static TextKey For(UserMailStatus status) => status switch
    {
        UserMailStatus.Sent => StatusSent,
        UserMailStatus.Skipped => StatusSkipped,
        UserMailStatus.Failed => StatusFailed,
        UserMailStatus.Bounced => StatusBounced,
        _ => StatusQueued
    };

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, Explanation, Empty, ShowMore, NotPermitted, NoUser, To, KindMail, KindMessage, KindNotice, KindInvitation,
        StatusQueued, StatusSent, StatusSkipped, StatusFailed, StatusBounced
    ];
}
