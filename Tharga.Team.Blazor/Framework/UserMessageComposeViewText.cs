namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserMessageComposeView</c>, where staff write a message to chosen users.</summary>
public static class UserMessageComposeViewText
{
    /// <summary>When the reader may not write messages to users.</summary>
    public static readonly TextKey NotPermitted = new("team.userMessage.compose.notPermitted", "You may not write messages to users.");

    /// <summary>One sentence above the form.</summary>
    public static readonly TextKey Explanation = new("team.userMessage.compose.explanation", "Write to users of the teams you choose. Each of them receives it as a mail.");

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

    /// <summary>Names the choice of specific users.</summary>
    public static readonly TextKey Users = new("team.userMessage.compose.users", "Specific users");

    /// <summary>The placeholder of the box that searches for users.</summary>
    public static readonly TextKey SearchUsers = new("team.userMessage.compose.searchUsers", "Search by name or address");

    /// <summary>When no user in the chosen teams matches the search.</summary>
    public static readonly TextKey NoUserMatches = new("team.userMessage.compose.noUserMatches", "No user matches.");

    /// <summary>The label of the button that takes a chosen user off the message; <c>{0}</c> is their name.</summary>
    public static readonly TextKey RemoveUser = new("team.userMessage.compose.removeUser", "Remove {0}");

    /// <summary>Names the category picker.</summary>
    public static readonly TextKey Category = new("team.userMessage.compose.category", "Category");

    /// <summary>Names the subject field.</summary>
    public static readonly TextKey Subject = new("team.userMessage.compose.subject", "Subject");

    /// <summary>Names the message editor.</summary>
    public static readonly TextKey Body = new("team.userMessage.compose.body", "Message");

    /// <summary>Asks for teams before anything can be counted.</summary>
    public static readonly TextKey NoTeams = new("team.userMessage.compose.noTeams", "Choose the teams to write within, or all teams.");

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
        NotPermitted, Explanation, Teams, AllTeams, ChooseTeams, Who, Everyone, AccessLevels, Roles, ChooseRoles, Users, SearchUsers, NoUserMatches, RemoveUser,
        Category, Subject, Body, NoTeams, Reaches, LeftOut, Send, ConfirmTitle, ConfirmBody, ConfirmSend, ConfirmCancel, Sent, SendFailed
    ];
}
