namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportQueueView</c> — the back-office view of a team's support cases.
/// </summary>
/// <remarks>
/// Separate from <see cref="SupportCasesViewText"/> on purpose. A support agent is told what is waiting and
/// what they are about to do to somebody else's case; a customer is told what happened to their own. Sharing
/// keys would force one phrasing to serve a person working a queue and a person asking for help.
/// </remarks>
public static class SupportQueueViewText
{
    public static readonly TextKey Title = new("team.support.queue.title", "Support cases");

    /// <summary>The count of cases whose newest entry came from the person who raised them.</summary>
    public static readonly TextKey Awaiting = new("team.support.queue.awaiting", "awaiting an answer");

    public static readonly TextKey NoCases = new("team.support.queue.none", "No support cases in this team.");

    public static readonly TextKey RaisedBy = new("team.support.queue.raisedBy", "raised by");

    public static readonly TextKey AnswerLabel = new("team.support.queue.answer", "Answer");

    public static readonly TextKey Close = new("team.support.queue.close", "Close case");

    public static readonly TextKey Reopen = new("team.support.queue.reopen", "Reopen");

    public static readonly TextKey Open = new("team.support.queue.status.open", "Open");

    public static readonly TextKey Closed = new("team.support.queue.status.closed", "Closed");

    public static readonly TextKey ClosedForInactivity = new("team.support.queue.status.inactivity",
        "Closed automatically after inactivity");

    public static readonly TextKey SelectTeam = new("team.support.queue.selectTeam", "Select a team to see its support cases.");

    /// <summary>Title of the confirmation shown before support closes a case.</summary>
    public static readonly TextKey ConfirmCloseTitle = new("team.support.queue.confirmClose.title", "Close this case?");

    /// <summary>
    /// The body of that confirmation.
    /// </summary>
    /// <remarks>
    /// <b>It suggests the alternative rather than only warning.</b> A case closed while the customer is still
    /// typing reads as being dismissed, and the person best placed to say the problem is solved is the person
    /// who had it. So this offers the better move — answer and let them close it — and still lets support
    /// close a case that is genuinely finished.
    /// </remarks>
    public static readonly TextKey ConfirmCloseBody = new("team.support.queue.confirmClose.body",
        "The person who raised this case can close it themselves once they are satisfied, which avoids closing it while they are still waiting. Close it anyway?");

    public static readonly TextKey ConfirmCloseAccept = new("team.support.queue.confirmClose.accept", "Close it");

    public static readonly TextKey ConfirmCloseCancel = new("team.support.queue.confirmClose.cancel", "Leave it open");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, Awaiting, NoCases, RaisedBy, AnswerLabel, Close, Reopen, Open, Closed, ClosedForInactivity,
        SelectTeam, ConfirmCloseTitle, ConfirmCloseBody, ConfirmCloseAccept, ConfirmCloseCancel
    ];
}
