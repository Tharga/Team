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

    public static readonly TextKey NewCase = new("team.support.cases.newCase", "Ask for help");

    public static readonly TextKey SubjectLabel = new("team.support.cases.subject", "Subject");

    public static readonly TextKey MessageLabel = new("team.support.cases.message", "What is wrong?");

    public static readonly TextKey Send = new("team.support.cases.send", "Send");

    public static readonly TextKey MyCases = new("team.support.cases.mine", "My cases");

    /// <summary>Shown instead of the list when this person has never raised a case.</summary>
    /// <remarks>
    /// Says what to do rather than that there is nothing — an empty list with the word "none" reads as a
    /// feature that is not working.
    /// </remarks>
    public static readonly TextKey NoCases = new("team.support.cases.none",
        "You have not asked for help yet. Describe the problem above and support will answer here.");

    public static readonly TextKey ReplyLabel = new("team.support.cases.reply", "Reply");

    public static readonly TextKey Reopen = new("team.support.cases.reopen", "Reopen");

    public static readonly TextKey Open = new("team.support.cases.status.open", "Open");

    public static readonly TextKey Closed = new("team.support.cases.status.closed", "Closed");

    /// <summary>
    /// What a case closed by the sweep says, as distinct from one somebody closed.
    /// </summary>
    /// <remarks>
    /// It must say that reopening is available in the same breath. "Closed automatically" on its own reads
    /// as being dismissed, which is the opposite of what an inactivity closure means.
    /// </remarks>
    public static readonly TextKey ClosedForInactivity = new("team.support.cases.status.inactivity",
        "Closed automatically — reopen it if the problem is still there.");

    public static readonly TextKey Unread = new("team.support.cases.unread", "New reply");

    public static readonly TextKey SelectTeam = new("team.support.cases.selectTeam", "Select a team to use support.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, NewCase, SubjectLabel, MessageLabel, Send, MyCases, NoCases, ReplyLabel, Reopen,
        Open, Closed, ClosedForInactivity, Unread, SelectTeam
    ];
}
