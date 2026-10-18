namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserMessageComposeView</c>, where staff write a message to chosen users.</summary>
public static class UserMessageComposeViewText
{
    /// <summary>When the reader may not write messages to users.</summary>
    public static readonly TextKey NotPermitted = new("team.userMessage.compose.notPermitted", "You may not write messages to users.");

    /// <summary>One sentence above the form.</summary>
    public static readonly TextKey Explanation = new("team.userMessage.compose.explanation", "Write to users of the teams you choose. Each of them receives it as a mail.");

    /// <summary>The heading of the framed filter.</summary>
    public static readonly TextKey Filter = new("team.userMessage.compose.filter", "Filter");

    /// <summary>What the filter does, under its heading.</summary>
    public static readonly TextKey FilterExplanation = new("team.userMessage.compose.filterExplanation", "Everyone the filter matches in the chosen teams is on the list.");

    /// <summary>Names the choice of teams.</summary>
    public static readonly TextKey Teams = new("team.userMessage.compose.teams", "Teams");

    /// <summary>The switch that writes within every team.</summary>
    public static readonly TextKey AllTeams = new("team.userMessage.compose.allTeams", "All teams");

    /// <summary>The placeholder of the team picker.</summary>
    public static readonly TextKey ChooseTeams = new("team.userMessage.compose.chooseTeams", "Choose teams");

    /// <summary>Names the choice of who in the teams.</summary>
    public static readonly TextKey Who = new("team.userMessage.compose.who", "Who");

    /// <summary>The switch that reaches everyone in the teams.</summary>
    public static readonly TextKey Everyone = new("team.userMessage.compose.everyone", "Everyone in the teams");

    /// <summary>Names the choice of access levels.</summary>
    public static readonly TextKey AccessLevels = new("team.userMessage.compose.accessLevels", "Everyone at an access level");

    /// <summary>Names the choice of roles.</summary>
    public static readonly TextKey Roles = new("team.userMessage.compose.roles", "Everyone holding a role");

    /// <summary>The placeholder of the role picker.</summary>
    public static readonly TextKey ChooseRoles = new("team.userMessage.compose.chooseRoles", "Choose roles");

    /// <summary>The heading over the search that adds users one by one.</summary>
    public static readonly TextKey Users = new("team.userMessage.compose.addUsers", "Add users");

    /// <summary>What adding a user does, under its heading.</summary>
    public static readonly TextKey AddedExplanation = new("team.userMessage.compose.addedExplanation", "A user added is on the list whatever the filter says, in any team.");

    /// <summary>The placeholder of the box that searches for users.</summary>
    public static readonly TextKey SearchUsers = new("team.userMessage.compose.searchUsers", "Search by name or address");

    /// <summary>When no user matches the search.</summary>
    public static readonly TextKey NoUserMatches = new("team.userMessage.compose.noUserMatches", "No user matches.");

    /// <summary>The label of the button that takes an added user off the message; <c>{0}</c> is their name.</summary>
    public static readonly TextKey RemoveUser = new("team.userMessage.compose.removeUser", "Remove {0}");

    /// <summary>The heading of the list of who receives the message.</summary>
    public static readonly TextKey RecipientList = new("team.userMessage.compose.recipientList", "Recipients");

    /// <summary>Marks a recipient the filter selected.</summary>
    public static readonly TextKey ByFilter = new("team.userMessage.compose.byFilter", "By filter");

    /// <summary>Marks a recipient added by name.</summary>
    public static readonly TextKey Added = new("team.userMessage.compose.added", "Added");

    /// <summary>Marks a recipient who turned the category off.</summary>
    public static readonly TextKey OptedOut = new("team.userMessage.compose.optedOut", "Will not receive it");

    /// <summary>Why an opted-out recipient will not receive it; <c>{0}</c> is the category's name.</summary>
    public static readonly TextKey OptedOutDetail = new("team.userMessage.compose.optedOutDetail", "Has turned off {0}.");

    /// <summary>When the selection reaches nobody.</summary>
    public static readonly TextKey NoRecipients = new("team.userMessage.compose.noRecipients", "Nobody is on the list.");

    /// <summary>The button that reads the next page of the list.</summary>
    public static readonly TextKey MoreRecipients = new("team.userMessage.compose.moreRecipients", "Show more");

    /// <summary>Names the category picker.</summary>
    public static readonly TextKey Category = new("team.userMessage.compose.category", "Category");

    /// <summary>Names the subject field.</summary>
    public static readonly TextKey Subject = new("team.userMessage.compose.subject", "Subject");

    /// <summary>Names the message editor.</summary>
    public static readonly TextKey Body = new("team.userMessage.compose.body", "Message");

    /// <summary>Asks for a selection before anything can be listed.</summary>
    public static readonly TextKey NoTeams = new("team.userMessage.compose.nothingChosen", "Choose teams and who in them, or add users, to see who receives it.");

    /// <summary>How many the selection reaches; <c>{0}</c> is the count.</summary>
    public static readonly TextKey Reaches = new("team.userMessage.compose.reaches", "Reaches {0} people.");

    /// <summary>How many of them turned the category off; <c>{0}</c> is the count, <c>{1}</c> the category's name.</summary>
    public static readonly TextKey LeftOut = new("team.userMessage.compose.leftOut", "{0} of them have turned off {1} and will not receive it.");

    /// <summary>The button that sends.</summary>
    public static readonly TextKey Send = new("team.userMessage.compose.send", "Send");

    /// <summary>The confirmation's title.</summary>
    public static readonly TextKey ConfirmTitle = new("team.userMessage.compose.confirmTitle", "Send message");

    /// <summary>The confirmation's question; <c>{0}</c> is the subject, <c>{1}</c> how many people it reaches.</summary>
    public static readonly TextKey ConfirmBody = new("team.userMessage.compose.confirmBody", "Send \"{0}\" to {1} people?");

    /// <summary>The confirmation's button that sends.</summary>
    public static readonly TextKey ConfirmSend = new("team.userMessage.compose.confirmSend", "Send");

    /// <summary>The confirmation's button that does not send.</summary>
    public static readonly TextKey ConfirmCancel = new("team.userMessage.compose.confirmCancel", "Cancel");

    /// <summary>Shown once the message is queued; <c>{0}</c> is how many it reaches.</summary>
    public static readonly TextKey Sent = new("team.userMessage.compose.sent", "The message is on its way to {0} people.");

    /// <summary>Shown when the message was refused; <c>{0}</c> is why.</summary>
    public static readonly TextKey SendFailed = new("team.userMessage.compose.sendFailed", "The message was not sent. {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        NotPermitted, Explanation, Filter, FilterExplanation, Teams, AllTeams, ChooseTeams, Who, Everyone, AccessLevels, Roles, ChooseRoles,
        Users, AddedExplanation, SearchUsers, NoUserMatches, RemoveUser, RecipientList, ByFilter, Added, OptedOut, OptedOutDetail, NoRecipients,
        MoreRecipients, Category, Subject, Body, NoTeams, Reaches, LeftOut, Send, ConfirmTitle, ConfirmBody, ConfirmSend, ConfirmCancel, Sent,
        SendFailed
    ];
}
