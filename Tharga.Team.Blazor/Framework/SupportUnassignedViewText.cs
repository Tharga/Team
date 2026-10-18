namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportUnassignedView</c> — the queue of support cases that belong to
/// no team.
/// </summary>
/// <remarks>
/// Separate from <see cref="SupportQueueViewText"/> because the reader is answering a different question. A
/// team's queue asks "what is waiting"; this asks "whose is this" — so its words are about provenance and
/// about the decision to give a case a team, and there is nothing to gain from making one phrasing serve
/// both.
/// </remarks>
public static class SupportUnassignedViewText
{
    /// <summary>The queue's name, for a host that labels the place it shows it; the view itself carries no heading.</summary>
    public static readonly TextKey Title = new("team.support.unassigned.title", "Unassigned support cases");

    /// <summary>
    /// Explains the queue rather than only labelling it.
    /// </summary>
    /// <remarks>
    /// <b>The reader has to know that leaving a case here is allowed.</b> Every earlier design treated "no
    /// team" as a staging area, and an operator who believes that will assign a case to whichever team looks
    /// plausible — which is the guess that puts one customer's problem in another customer's list.
    /// </remarks>
    public static readonly TextKey Explanation = new("team.support.unassigned.explanation",
        "These arrived without a team — usually by email from a sender whose team could not be determined. Answer them here, and give one a team only when you know which it is.");

    public static readonly TextKey NoCases = new("team.support.unassigned.none", "Nothing is waiting without a team.");

    /// <summary>Shown when there are cases but the filter hides every one.</summary>
    public static readonly TextKey NoneMatch = new("team.support.unassigned.noneMatch", "No case matches the filter.");

    /// <summary>Follows the number of open cases whose newest entry is from the sender.</summary>
    public static readonly TextKey Awaiting = new("team.support.unassigned.awaiting", "awaiting an answer");

    /// <summary>Marks a case whose newest entry is from the sender, so support sees what to answer.</summary>
    public static readonly TextKey NeedsAnswer = new("team.support.unassigned.needsAnswer", "Needs an answer");

    public static readonly TextKey From = new("team.support.unassigned.from", "from");

    public static readonly TextKey AnswerLabel = new("team.support.unassigned.answer", "Answer");

    public static readonly TextKey Close = new("team.support.unassigned.close", "Close case");

    public static readonly TextKey Reopen = new("team.support.unassigned.reopen", "Reopen");

    public static readonly TextKey Open = new("team.support.unassigned.status.open", "Open");

    public static readonly TextKey Closed = new("team.support.unassigned.status.closed", "Closed");

    public static readonly TextKey ClosedForInactivity = new("team.support.unassigned.status.inactivity",
        "Closed automatically after inactivity");

    public static readonly TextKey AssignLabel = new("team.support.unassigned.assign", "Assign to team");

    public static readonly TextKey AssignAction = new("team.support.unassigned.assign.action", "Assign");

    /// <summary>Shown when another operator assigned the case first.</summary>
    /// <remarks>
    /// A queue that silently swallows the second click is how an operator stops trusting it, so the loser of
    /// the race is told what happened rather than left looking at a button that did nothing.
    /// </remarks>
    public static readonly TextKey AlreadyAssigned = new("team.support.unassigned.assign.taken",
        "Somebody assigned this case first. The queue has been refreshed.");

    /// <summary>Shown when the caller can read the queue but cannot list teams to assign one.</summary>
    public static readonly TextKey CannotListTeams = new("team.support.unassigned.assign.noTeams",
        "Assigning a case needs the cross-team read grant, which you do not hold. You can still answer and close cases here.");

    /// <summary>Shown when the caller holds neither unassigned-queue grant.</summary>
    public static readonly TextKey NotPermitted = new("team.support.unassigned.notPermitted",
        "You do not have access to support cases that belong to no team.");

    /// <summary>Heads the teams the sender's address matched.</summary>
    public static readonly TextKey Suggested = new("team.support.unassigned.suggested", "Suggested teams");

    /// <summary>Why the suggestions are not the case's team: the sender belongs to several.</summary>
    /// <remarks>
    /// Each reason says what was <i>not</i> established, because that is what the reader has to supply. A
    /// suggestion without its reason reads as the toolkit's recommendation, and an operator who trusts it is
    /// back to guessing.
    /// </remarks>
    public static readonly TextKey ReasonSeveralTeams = new("team.support.unassigned.reason.severalTeams",
        "The sender is verified and belongs to more than one team.");

    /// <summary>Why the suggestions are not the case's team: the mail could not be verified.</summary>
    public static readonly TextKey ReasonSenderNotVerified = new("team.support.unassigned.reason.notVerified",
        "The sender's address belongs to these teams, but the mail could not be verified as coming from it.");

    /// <summary>Why the suggestions are not the case's team: more than one account holds the address.</summary>
    public static readonly TextKey ReasonSharedAddress = new("team.support.unassigned.reason.sharedAddress",
        "More than one user account has the sender's address.");

    /// <summary>The one-click action giving the case a suggested team; <c>{0}</c> is the team's name.</summary>
    public static readonly TextKey AssignToSuggested = new("team.support.unassigned.suggested.assign", "Assign to {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, Explanation, NoCases, NoneMatch, Awaiting, NeedsAnswer, From, AnswerLabel, Close, Reopen, Open, Closed, ClosedForInactivity,
        AssignLabel, AssignAction, AlreadyAssigned, CannotListTeams, NotPermitted,
        Suggested, ReasonSeveralTeams, ReasonSenderNotVerified, ReasonSharedAddress, AssignToSuggested
    ];
}
