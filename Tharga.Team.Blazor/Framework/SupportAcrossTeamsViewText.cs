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
    public static readonly TextKey Title = new("team.support.acrossTeams.title", "Support cases in every team");

    public static readonly TextKey Awaiting = new("team.support.acrossTeams.awaiting", "awaiting an answer");

    public static readonly TextKey NoCases = new("team.support.acrossTeams.none", "No team has raised a support case.");

    public static readonly TextKey RaisedBy = new("team.support.acrossTeams.raisedBy", "raised by");

    public static readonly TextKey InTeam = new("team.support.acrossTeams.inTeam", "in");

    public static readonly TextKey AnswerLabel = new("team.support.acrossTeams.answer", "Answer");

    public static readonly TextKey Close = new("team.support.acrossTeams.close", "Close case");

    public static readonly TextKey Reopen = new("team.support.acrossTeams.reopen", "Reopen");

    public static readonly TextKey Open = new("team.support.acrossTeams.status.open", "Open");

    public static readonly TextKey Closed = new("team.support.acrossTeams.status.closed", "Closed");

    public static readonly TextKey ClosedForInactivity = new("team.support.acrossTeams.status.inactivity",
        "Closed automatically after inactivity");

    /// <summary>Shown when the caller holds neither cross-team grant.</summary>
    public static readonly TextKey NotPermitted = new("team.support.acrossTeams.notPermitted",
        "You do not have access to support cases across every team.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Title, Awaiting, NoCases, RaisedBy, InTeam, AnswerLabel, Close, Reopen, Open, Closed, ClosedForInactivity,
        NotPermitted
    ];
}
