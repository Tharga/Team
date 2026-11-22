namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>SupportAssistantLimitView</c> — what the platform's staff let each team spend on the assistant.</summary>
public static class SupportAssistantLimitViewText
{
    public static readonly TextKey NotPermitted = new("team.support.limits.notPermitted", "Setting the assistant's limits needs the support:allowance:manage system scope.");
    public static readonly TextKey DefaultTitle = new("team.support.limits.defaultTitle", "Default limit");
    public static readonly TextKey DefaultExplanation = new("team.support.limits.defaultExplanation", "What a team may spend on the assistant each month when it has no limit of its own. Amounts are money, in the catalogue's currency; teams are shown the share they have used, in the unit the host chose.");
    public static readonly TextKey Limit = new("team.support.limits.limit", "Limit per month");
    public static readonly TextKey NoLimit = new("team.support.limits.noLimit", "No limit");
    public static readonly TextKey Current = new("team.support.limits.current", "Now: {0}");
    public static readonly TextKey FromHere = new("team.support.limits.fromHere", "Set here.");
    public static readonly TextKey FromHost = new("team.support.limits.fromHost", "From the host's setting, until one is set here.");
    public static readonly TextKey SaveDefault = new("team.support.limits.saveDefault", "Save default");
    public static readonly TextKey UseHostSetting = new("team.support.limits.useHostSetting", "Use the host's setting");
    public static readonly TextKey TeamsTitle = new("team.support.limits.teamsTitle", "Limits of single teams");
    public static readonly TextKey TeamsExplanation = new("team.support.limits.teamsExplanation", "A team with a limit of its own is held to it instead of the default, whether higher, lower or none. Removing it puts the team back on the default.");
    public static readonly TextKey Team = new("team.support.limits.team", "Team");
    public static readonly TextKey NoTeams = new("team.support.limits.noTeams", "No team has a limit of its own. All are held to the default.");
    public static readonly TextKey SaveTeam = new("team.support.limits.saveTeam", "Save limit for team");
    public static readonly TextKey Edit = new("team.support.limits.edit", "Edit");
    public static readonly TextKey UseDefault = new("team.support.limits.useDefault", "Use default");
    public static readonly TextKey ChooseTeam = new("team.support.limits.chooseTeam", "Choose a team.");
    public static readonly TextKey EnterAmount = new("team.support.limits.enterAmount", "Enter an amount, or choose no limit.");
    public static readonly TextKey Saved = new("team.support.limits.saved", "Saved");
    public static readonly TextKey Failed = new("team.support.limits.failed", "Something went wrong.");

    public static readonly TextKey[] All =
    [
        NotPermitted, DefaultTitle, DefaultExplanation, Limit, NoLimit, Current, FromHere, FromHost, SaveDefault, UseHostSetting, TeamsTitle, TeamsExplanation,
        Team, NoTeams, SaveTeam, Edit, UseDefault, ChooseTeam, EnterAmount, Saved, Failed
    ];
}
