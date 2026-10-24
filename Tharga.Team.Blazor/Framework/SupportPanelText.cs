namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>SupportPanel</c> — help beside every page, from the assistant or a person.</summary>
public static class SupportPanelText
{
    /// <summary>The rail button and tab that talk to the assistant.</summary>
    public static readonly TextKey Assistant = new("team.support.panel.assistant", "Assistant");

    /// <summary>The rail button and tab that write to a person.</summary>
    public static readonly TextKey Support = new("team.support.panel.support", "Support");

    /// <summary>The label of the choice between the two.</summary>
    public static readonly TextKey WhoToAsk = new("team.support.panel.whoToAsk", "Who to ask");

    /// <summary>Starts a new conversation.</summary>
    public static readonly TextKey NewConversation = new("team.support.panel.newConversation", "New conversation");

    /// <summary>Opens the list of earlier conversations.</summary>
    public static readonly TextKey Earlier = new("team.support.panel.earlier", "Earlier conversations");

    /// <summary>When there are no earlier conversations.</summary>
    public static readonly TextKey NoEarlier = new("team.support.panel.noEarlier", "Nothing yet. Write below and it starts one.");

    /// <summary>Goes to the host's full support page.</summary>
    public static readonly TextKey AllConversations = new("team.support.panel.allConversations", "All conversations");

    /// <summary>Collapses the panel to its rail.</summary>
    public static readonly TextKey Close = new("team.support.panel.close", "Close");

    /// <summary>The handle that changes the panel's width.</summary>
    public static readonly TextKey Resize = new("team.support.panel.resize", "Drag to resize");

    /// <summary>The first line of a conversation with the assistant.</summary>
    public static readonly TextKey AssistantGreeting = new("team.support.panel.assistantGreeting", "How can I help you?");

    /// <summary>The first line of a conversation with support.</summary>
    public static readonly TextKey SupportGreeting = new("team.support.panel.supportGreeting", "Write to us and somebody will answer as soon as possible.");

    /// <summary>The message box's label.</summary>
    public static readonly TextKey Message = new("team.support.panel.message", "Your message");

    /// <summary>The message box's placeholder when talking to the assistant.</summary>
    public static readonly TextKey AskAQuestion = new("team.support.panel.askAQuestion", "Ask a question…");

    /// <summary>The message box's placeholder when writing to support.</summary>
    public static readonly TextKey WriteToSupport = new("team.support.panel.writeToSupport", "Write to support…");

    /// <summary>Sends the message.</summary>
    public static readonly TextKey Send = new("team.support.panel.send", "Send");

    /// <summary>While the assistant is answering.</summary>
    public static readonly TextKey Working = new("team.support.panel.working", "Working…");

    /// <summary>When a message could not be sent.</summary>
    public static readonly TextKey Failed = new("team.support.panel.failed", "Something went wrong. Please try again.");

    /// <summary>When no team is selected.</summary>
    public static readonly TextKey SelectTeam = new("team.support.panel.selectTeam", "Select a team to ask for help.");

    /// <summary>Every key the component renders, its own and the shared decline notice, for building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Assistant, Support, WhoToAsk, NewConversation, Earlier, NoEarlier, AllConversations, Close, Resize, AssistantGreeting,
        SupportGreeting, Message, AskAQuestion, WriteToSupport, Send, Working, Failed, SelectTeam, SupportCasesViewText.AssistantDeclined,
        SupportTranscriptText.Typing, SupportTranscriptText.Round, SupportTranscriptText.RoundTools, SupportTranscriptText.Helpful,
        SupportTranscriptText.NotHelpful, SupportTranscriptText.NoAnswerYet, SupportTranscriptText.NoAnswerAway
    ];
}
