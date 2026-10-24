namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportCasesView</c> — the customer's own view of their cases.
/// </summary>
/// <remarks>
/// Separate from the back-office wording on purpose. The customer is told what happened to *their* case; a
/// support agent is told what is waiting. Sharing keys would force one phrasing to serve a person asking for
/// help and a person working a queue.
/// </remarks>
public static class SupportCasesViewText
{
    public static readonly TextKey Title = new("team.support.cases.title", "Support");

    /// <summary>Heads the new-case form, followed by the choice of who answers.</summary>
    public static readonly TextKey NewCase = new("team.support.cases.newCase", "Talk to");

    public static readonly TextKey SubjectLabel = new("team.support.cases.subject", "Subject");

    public static readonly TextKey MessageLabel = new("team.support.cases.message.help", "How can we help you?");

    public static readonly TextKey Send = new("team.support.cases.send", "Send");

    /// <summary>Names the tab that lists every conversation, which comes first.</summary>
    public static readonly TextKey CasesTab = new("team.support.cases.tab.cases", "Cases");

    /// <summary>Shown instead of the list when this person has never raised a case.</summary>
    /// <remarks>
    /// Says what to do rather than that there is nothing — an empty list with the word "none" reads as a
    /// feature that is not working.
    /// </remarks>
    public static readonly TextKey NothingYet = new("team.support.cases.empty",
        "You have not asked for help yet. Start a conversation under Talk to, and the answer comes here.");

    /// <summary>Shown when there are cases but the filter hides every one.</summary>
    public static readonly TextKey NoneMatch = new("team.support.cases.noneMatch", "No case matches the filter.");

    public static readonly TextKey ReplyLabel = new("team.support.cases.reply", "Reply");

    /// <summary>Lets the person who raised a case close it once it is solved.</summary>
    public static readonly TextKey Close = new("team.support.cases.close", "Close case");

    public static readonly TextKey CloseConfirmTitle = new("team.support.cases.close.title", "Close the case?");

    /// <summary>Says closing is not final, so it is not put off for fear of losing the conversation.</summary>
    public static readonly TextKey CloseConfirmBody = new("team.support.cases.close.body",
        "Close it once the problem is solved. Writing to it again opens it again.");

    public static readonly TextKey Cancel = new("team.support.cases.cancel", "Cancel");

    public static readonly TextKey Open = new("team.support.cases.status.open", "Open");

    public static readonly TextKey Closed = new("team.support.cases.status.closed", "Closed");

    /// <summary>What a conversation with the assistant says in place of a status: it is neither open nor closed.</summary>
    public static readonly TextKey WithAssistant = new("team.support.cases.status.assistant", "Assistant conversation");

    /// <summary>
    /// What a case closed by the sweep says, as distinct from one somebody closed.
    /// </summary>
    /// <remarks>
    /// It must say that reopening is available in the same breath. "Closed automatically" on its own reads
    /// as being dismissed, which is the opposite of what an inactivity closure means.
    /// </remarks>
    public static readonly TextKey ClosedForInactivity = new("team.support.cases.status.inactivity",
        "Closed automatically — reopen it if the problem is still there.");

    public static readonly TextKey SelectTeam = new("team.support.cases.selectTeam", "Select a team to use support.");

    /// <summary>Shown when somebody on support is active.</summary>
    public static readonly TextKey SupportOnline = new("team.support.cases.presence.online", "Support is online");

    /// <summary>
    /// Shown when support is reachable but nobody is active.
    /// </summary>
    /// <remarks>
    /// Says an answer is coming later rather than that nobody is there — the case is still raised and still
    /// answered, so wording that reads as "do not bother" would be wrong as well as discouraging.
    /// <para>
    /// There is deliberately no key for unknown presence. It renders nothing at all.
    /// </para>
    /// </remarks>
    public static readonly TextKey SupportAway = new("team.support.cases.presence.away", "Support will reply later");

    /// <summary>The assistant, as a choice of who answers and as a kind of conversation in the list.</summary>
    public static readonly TextKey AnsweredByAssistant = new("team.support.cases.answeredBy.assistant", "Assistant");

    /// <summary>A person on support, as a choice of who answers and as a kind of conversation in the list.</summary>
    public static readonly TextKey AnsweredByHuman = new("team.support.cases.answeredBy.human", "Support");

    /// <summary>Read out for the dots shown while the assistant is working, so a slow model does not read as a broken page.</summary>
    public static readonly TextKey AssistantThinking = new("team.support.cases.assistant.thinking", "Working");

    /// <summary>The hand-over action, offered only while an assistant is answering.</summary>
    public static readonly TextKey TalkToAPerson = new("team.support.cases.assistant.handover", "Talk to a person");

    /// <summary>Shown when the assistant was asked and did not answer, beside the hand-over action.</summary>
    public static readonly TextKey AssistantDeclined = new("team.support.cases.assistant.declined",
        "The assistant could not answer this. Talk to a person instead.");

    /// <summary>Introduces the choice of who in the team may read the case, offered while it is open.</summary>
    public static readonly TextKey VisibilityLabel = new("team.support.cases.visibility.label", "Who can read this case");

    public static readonly TextKey VisibilityTeam = new("team.support.cases.visibility.team", "Everyone in your team");

    public static readonly TextKey VisibilityPrivate = new("team.support.cases.visibility.private", "Only you");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, NewCase, SubjectLabel, MessageLabel, Send, CasesTab, NothingYet, NoneMatch, ReplyLabel, Close, CloseConfirmTitle, CloseConfirmBody, Cancel,
        Open, Closed, WithAssistant, ClosedForInactivity, SelectTeam, SupportOnline, SupportAway,
        AnsweredByAssistant, AnsweredByHuman, AssistantThinking, TalkToAPerson, AssistantDeclined,
        VisibilityLabel, VisibilityTeam, VisibilityPrivate
    ];
}
