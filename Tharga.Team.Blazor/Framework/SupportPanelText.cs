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

    /// <summary>Before the assistant's dots while it answers; the dots stand for the "…".</summary>
    public static readonly TextKey Working = new("team.support.panel.working", "Working");

    /// <summary>When a message could not be sent.</summary>
    public static readonly TextKey Failed = new("team.support.panel.failed", "Something went wrong. Please try again.");

    /// <summary>When no team is selected.</summary>
    public static readonly TextKey SelectTeam = new("team.support.panel.selectTeam", "Select a team to ask for help.");

    /// <summary>Asks how the assistant did in the conversation as a whole.</summary>
    public static readonly TextKey RateConversation = new("team.support.panel.rateConversation", "How did the assistant do in this conversation?");

    /// <summary>The thumbs-up for the whole conversation.</summary>
    public static readonly TextKey ConversationGood = new("team.support.panel.conversationGood", "Good");

    /// <summary>The thumbs-down for the whole conversation, which asks why.</summary>
    public static readonly TextKey ConversationNotGood = new("team.support.panel.conversationNotGood", "Not good");

    /// <summary>The placeholder of the comment asked for on thumbs-down.</summary>
    public static readonly TextKey ConversationComment = new("team.support.panel.conversationComment", "What could the assistant have done better? (optional)");

    /// <summary>Beside the comment: what the feedback is used for. A host names itself here.</summary>
    public static readonly TextKey ConversationNotice = new("team.support.panel.conversationNotice", "This information will be used to improve how the assistant works.");

    /// <summary>Sends the thumbs-down and its comment.</summary>
    public static readonly TextKey SendConversationRating = new("team.support.panel.sendConversationRating", "Send");

    /// <summary>Once the conversation is rated.</summary>
    public static readonly TextKey ConversationThanks = new("team.support.panel.conversationThanks", "Thank you for your feedback.");

    /// <summary>The label of the allowance bar, for a screen reader.</summary>
    public static readonly TextKey AllowanceLabel = new("team.support.panel.allowanceLabel", "Assistant allowance");

    /// <summary>Under the bar: the share of the allowance spent. {0} is a number from 0 up.</summary>
    public static readonly TextKey AllowanceShare = new("team.support.panel.allowanceShare", "{0}% used");

    /// <summary>Under the bar, for a team's administrators: what is spent of what may be. {0} is spent, {1} the limit, {2} the unit.</summary>
    public static readonly TextKey AllowanceAmounts = new("team.support.panel.allowanceAmounts", "{0} of {1} {2} used");

    /// <summary>Under the bar: when the allowance starts again. {0} is a date.</summary>
    public static readonly TextKey AllowanceRenews = new("team.support.panel.allowanceRenews", "Starts again {0}");

    /// <summary>Under the bar, for a team's administrators. {0} is how many questions, {1} the model.</summary>
    public static readonly TextKey AllowanceQuestionsLeft = new("team.support.panel.allowanceQuestionsLeft", "About {0} questions left on {1}");

    /// <summary>In place of the conversation when the team has used its allowance. {0} is the date it starts again.</summary>
    public static readonly TextKey AllowanceUsedUp = new("team.support.panel.allowanceUsedUp", "The assistant has been used as much as this team's allowance lets it, so it cannot answer right now. It starts again {0}.");

    /// <summary>In place of the conversation when the provider has refused the account.</summary>
    public static readonly TextKey ProviderRefused = new("team.support.panel.providerRefused", "The assistant cannot answer right now, because the service behind it has refused the account. Please try again a little later.");

    /// <summary>In place of the conversation when the host holds the assistant back and gave no words of its own.</summary>
    public static readonly TextKey AssistantHeld = new("team.support.panel.assistantHeld", "The assistant is not available to this team right now.");

    /// <summary>Under the notice, only where the team has support to write to.</summary>
    public static readonly TextKey PausedWriteToSupport = new("team.support.panel.pausedWriteToSupport", "You can write to support meanwhile.");

    /// <summary>Every key the component renders, its own and the shared decline notice, for building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Assistant, Support, WhoToAsk, NewConversation, Earlier, NoEarlier, AllConversations, Close, Resize, AssistantGreeting,
        SupportGreeting, Message, AskAQuestion, WriteToSupport, Send, Working, Failed, SelectTeam, RateConversation, ConversationGood,
        ConversationNotGood, ConversationComment, ConversationNotice, SendConversationRating, ConversationThanks, SupportCasesViewText.AssistantDeclined,
        SupportTranscriptText.Typing, SupportTranscriptText.WorkingLoop, SupportTranscriptText.Thinking, SupportTranscriptText.ThinkingLoop, SupportTranscriptText.Helpful,
        SupportTranscriptText.NotHelpful, SupportTranscriptText.NoAnswerYet, SupportTranscriptText.NoAnswerAway,
        AllowanceLabel, AllowanceShare, AllowanceAmounts, AllowanceRenews, AllowanceQuestionsLeft, AllowanceUsedUp, ProviderRefused, AssistantHeld, PausedWriteToSupport
    ];
}
