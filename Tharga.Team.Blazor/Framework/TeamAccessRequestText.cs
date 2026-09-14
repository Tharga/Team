namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings for requesting access to a team and deciding requests — <c>TeamAccessRequestPanel</c>,
/// <c>TeamAccessRequestDialog</c> and <c>TeamNotificationMenu</c>.
/// </summary>
/// <remarks>
/// Sentences with a placeholder are whole sentences, so a translation can put the pieces where its language wants them.
/// </remarks>
public static class TeamAccessRequestText
{
    public static readonly TextKey Title = new("team.accessRequest.title", "Access requests");

    public static readonly TextKey RequestAccess = new("team.accessRequest.requestAccess", "Request access");

    /// <summary>{0} = the consent roles.</summary>
    public static readonly TextKey RequestAccessDescription = new("team.accessRequest.requestAccessDescription",
        "Ask this team's managers for access. If they approve, everyone with {0} gets that access for the time you choose.");

    /// <summary>{0} = access level, {1} = duration phrase ("for 8 hours").</summary>
    public static readonly TextKey YourPendingRequest = new("team.accessRequest.yourPendingRequest",
        "You have asked for {0} access {1}. Waiting for a team manager.");

    public static readonly TextKey Withdraw = new("team.accessRequest.withdraw", "Withdraw request");

    public static readonly TextKey AccessLevel = new("team.accessRequest.accessLevel", "Access level");

    public static readonly TextKey Duration = new("team.accessRequest.duration", "For how long");

    public static readonly TextKey Message = new("team.accessRequest.message", "Why do you need access? (optional)");

    public static readonly TextKey Send = new("team.accessRequest.send", "Send request");

    public static readonly TextKey Sent = new("team.accessRequest.sent", "Access requested. A team manager will decide.");

    /// <summary>{0} = requester, {1} = access level, {2} = duration phrase.</summary>
    public static readonly TextKey RequestLine = new("team.accessRequest.requestLine", "{0} asks for {1} access {2}");

    public static readonly TextKey Approve = new("team.accessRequest.approve", "Approve");

    public static readonly TextKey Deny = new("team.accessRequest.deny", "Deny");

    public static readonly TextKey ApproveTitle = new("team.accessRequest.approveTitle", "Approve access request");

    /// <summary>{0} = the consent roles, {1} = access level, {2} = team name, {3} = duration phrase.</summary>
    /// <remarks>The one place a manager learns approval is not only for the requester. Keep the "everyone".</remarks>
    public static readonly TextKey ApproveWarning = new("team.accessRequest.approveWarning",
        "Everyone with {0} will get {1} access to {2} {3} — not only the person who asked.");

    public static readonly TextKey Approved = new("team.accessRequest.approved", "Access approved.");

    public static readonly TextKey Denied = new("team.accessRequest.denied", "Access request denied.");

    public static readonly TextKey Withdrawn = new("team.accessRequest.withdrawn", "Request withdrawn.");

    public static readonly TextKey Failed = new("team.accessRequest.failed", "That did not work");

    public static readonly TextKey ForOneHour = new("team.accessRequest.forOneHour", "for 1 hour");

    /// <summary>{0} = number of hours.</summary>
    public static readonly TextKey ForHours = new("team.accessRequest.forHours", "for {0} hours");

    public static readonly TextKey ForOneDay = new("team.accessRequest.forOneDay", "for 1 day");

    /// <summary>{0} = number of days.</summary>
    public static readonly TextKey ForDays = new("team.accessRequest.forDays", "for {0} days");

    public static readonly TextKey WithNoEnd = new("team.accessRequest.withNoEnd", "with no end");

    public static readonly TextKey ChoiceOneHour = new("team.accessRequest.choiceOneHour", "1 hour");

    public static readonly TextKey ChoiceEightHours = new("team.accessRequest.choiceEightHours", "8 hours");

    public static readonly TextKey ChoiceOneDay = new("team.accessRequest.choiceOneDay", "1 day");

    public static readonly TextKey ChoiceOneWeek = new("team.accessRequest.choiceOneWeek", "1 week");

    public static readonly TextKey ChoiceThirtyDays = new("team.accessRequest.choiceThirtyDays", "30 days");

    public static readonly TextKey ChoiceNoEnd = new("team.accessRequest.choiceNoEnd", "No end");

    /// <summary>{0} = access level, {1} = end time, {2} = what it returns to.</summary>
    public static readonly TextKey TemporaryConsent = new("team.accessRequest.temporaryConsent",
        "Consent is temporary: {0} until {1}, then {2}.");

    public static readonly TextKey NoAccess = new("team.accessRequest.noAccess", "no access");

    public static readonly TextKey Notifications = new("team.accessRequest.notifications", "Notifications");

    public static readonly TextKey NothingWaiting = new("team.accessRequest.nothingWaiting", "Nothing is waiting for you.");

    /// <summary>{0} = team name.</summary>
    public static readonly TextKey YourRequestFor = new("team.accessRequest.yourRequestFor", "Your request for {0} is waiting.");

    /// <summary>Every key here, for the components building their <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, RequestAccess, RequestAccessDescription, YourPendingRequest, Withdraw, AccessLevel, Duration, Message, Send, Sent,
        RequestLine, Approve, Deny, ApproveTitle, ApproveWarning, Approved, Denied, Withdrawn, Failed,
        ForOneHour, ForHours, ForOneDay, ForDays, WithNoEnd,
        ChoiceOneHour, ChoiceEightHours, ChoiceOneDay, ChoiceOneWeek, ChoiceThirtyDays, ChoiceNoEnd,
        TemporaryConsent, NoAccess, Notifications, NothingWaiting, YourRequestFor
    ];
}
