namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportComposeMail</c>, the form support staff write first with, and by the
/// "New message" buttons that open it.
/// </summary>
public static class SupportComposeMailText
{
    /// <summary>The button on the support boards, and the title of the form opened by it.</summary>
    public static readonly TextKey NewMessage = new("team.support.compose.newMessage", "New message");

    /// <summary>Label of the recipient's address.</summary>
    public static readonly TextKey To = new("team.support.compose.to", "To");

    /// <summary>Label of the recipient's name.</summary>
    public static readonly TextKey Name = new("team.support.compose.name", "Name");

    /// <summary>Label of the team the case is placed in; shown when the reader can choose one.</summary>
    public static readonly TextKey Team = new("team.support.compose.team", "Team");

    /// <summary>Label of the subject.</summary>
    public static readonly TextKey Subject = new("team.support.compose.subject", "Subject");

    /// <summary>Label of the message box.</summary>
    public static readonly TextKey Message = new("team.support.compose.message", "Message");

    /// <summary>The send button.</summary>
    public static readonly TextKey Send = new("team.support.compose.send", "Send");

    /// <summary>The cancel button.</summary>
    public static readonly TextKey Cancel = new("team.support.compose.cancel", "Cancel");

    /// <summary>Shown when the form cannot be used because no mail is set up for support.</summary>
    public static readonly TextKey NoMail = new("team.support.compose.noMail", "No mail is set up for support, so a message cannot be sent from here.");

    /// <summary>Shown when the reader may not write first to anybody.</summary>
    public static readonly TextKey NotPermitted = new("team.support.compose.notPermitted", "You may not start a conversation by mail.");

    /// <summary>Shown when the address is not a single, plain address.</summary>
    public static readonly TextKey BadAddress = new("team.support.compose.badAddress", "Enter one email address.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [NewMessage, To, Name, Team, Subject, Message, Send, Cancel, NoMail, NotPermitted, BadAddress];
}
