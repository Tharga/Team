namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportAcrossTeamsView</c> — the product's own support staff working
/// every team's cases from one queue.
/// </summary>
/// <remarks>
/// Separate from <see cref="SupportQueueViewText"/> because the reader belongs to none of the teams: every
/// case has to say whose it is, and "select a team" means nothing here.
/// </remarks>
public static class SupportAcrossTeamsViewText
{
    public static readonly TextKey Awaiting = new("team.support.acrossTeams.awaiting", "awaiting an answer");

    public static readonly TextKey NoCases = new("team.support.acrossTeams.none", "No team has raised a support case.");

    public static readonly TextKey AnswerLabel = new("team.support.acrossTeams.answer", "Answer");

    public static readonly TextKey Close = new("team.support.acrossTeams.close", "Close case");

    public static readonly TextKey CloseWarningTitle = new("team.support.acrossTeams.close.title", "Close the organisation's case?");

    /// <summary>Closing is the team's word that the problem is solved, so staff are told before they say it for them.</summary>
    public static readonly TextKey CloseWarning = new("team.support.acrossTeams.close.warning",
        "It is better that the organisation closes the case themselves, once they agree the problem is solved.");

    public static readonly TextKey CloseAnyway = new("team.support.acrossTeams.close.anyway", "Close anyway");

    /// <summary>Names the one person who may read a case kept to its author.</summary>
    public static readonly TextKey OnlyTheAuthor = new("team.support.acrossTeams.onlyAuthor", "Only {0}");

    public static readonly TextKey Open = new("team.support.acrossTeams.status.open", "Open");

    public static readonly TextKey Closed = new("team.support.acrossTeams.status.closed", "Closed");

    /// <summary>What a conversation with the assistant says in place of a status: it is neither open nor closed.</summary>
    public static readonly TextKey WithAssistant = new("team.support.acrossTeams.status.assistant", "Assistant conversation");

    /// <summary>Shown when there are cases but the filter hides every one.</summary>
    public static readonly TextKey NoneMatch = new("team.support.acrossTeams.noneMatch", "No case matches the filter.");

    public static readonly TextKey ClosedForInactivity = new("team.support.acrossTeams.status.inactivity",
        "Closed automatically after inactivity");

    /// <summary>Marks a case whose newest entry is from the one who raised it, so staff see what to answer.</summary>
    public static readonly TextKey NeedsAnswer = new("team.support.acrossTeams.needsAnswer", "Needs an answer");

    /// <summary>A conversation with a person on support, as the mark on a case.</summary>
    public static readonly TextKey Support = new("team.support.acrossTeams.kind.support", "Support");

    /// <summary>A conversation the assistant is answering, as the mark on a case.</summary>
    public static readonly TextKey Assistant = new("team.support.acrossTeams.kind.assistant", "Assistant");

    /// <summary>Shown when the caller holds neither cross-team grant.</summary>
    public static readonly TextKey NotPermitted = new("team.support.acrossTeams.notPermitted",
        "You do not have access to support cases across every team.");

    /// <summary>On a case where the assistant was asked for something it had no way to do; what it was shows on hover.</summary>
    public static readonly TextKey AssistantCouldNot = new("team.support.acrossTeams.assistantCouldNot", "The assistant could not help");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Awaiting, NoCases, NoneMatch, AnswerLabel, Close, CloseWarningTitle, CloseWarning, CloseAnyway, OnlyTheAuthor, Open, Closed, WithAssistant,
        ClosedForInactivity, NotPermitted, NeedsAnswer, Support, Assistant, AssistantCouldNot
    ];
}
