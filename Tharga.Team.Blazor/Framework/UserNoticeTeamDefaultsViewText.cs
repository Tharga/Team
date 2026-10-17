namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserNoticeTeamDefaultsView</c> — what a team's members receive of automatic notices until they choose.</summary>
public static class UserNoticeTeamDefaultsViewText
{
    /// <summary>The section's title.</summary>
    public static readonly TextKey Title = new("team.userNotice.teamDefaults.title", "Notice defaults");

    /// <summary>One sentence above the list.</summary>
    public static readonly TextKey Explanation = new("team.userNotice.teamDefaults.explanation", "What members of the team receive until they choose for themselves. A change is saved at once.");

    /// <summary>Under a notice the team set something other than the catalogue's default for.</summary>
    public static readonly TextKey Changed = new("team.userNotice.teamDefaults.changed", "Changed for this team.");

    /// <summary>When the catalogue holds no notice.</summary>
    public static readonly TextKey Empty = new("team.userNotice.teamDefaults.empty", "There are no notices.");

    /// <summary>When no team is selected or named.</summary>
    public static readonly TextKey SelectTeam = new("team.userNotice.teamDefaults.selectTeam", "Select a team to set its notice defaults.");

    /// <summary>When a change could not be saved.</summary>
    public static readonly TextKey SaveFailed = new("team.userNotice.teamDefaults.saveFailed", "The default could not be saved.");

    /// <summary>When the reader may not set the team's defaults.</summary>
    public static readonly TextKey NotPermitted = new("team.userNotice.teamDefaults.notPermitted", "You cannot change this team's notice defaults.");

    /// <summary>Every key the component renders, its own and the shared delivery names, for building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Title, Explanation, Changed, Empty, SelectTeam, SaveFailed, NotPermitted, UserNoticeText.AtOnce, UserNoticeText.Digest, UserNoticeText.Delivery];
}
