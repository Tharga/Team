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

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, Explanation, NoCases, From, AnswerLabel, Close, Reopen, Open, Closed, ClosedForInactivity,
        AssignLabel, AssignAction, AlreadyAssigned, CannotListTeams, NotPermitted
    ];
}
