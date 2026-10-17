namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserNoticePreferencesView</c> — which automatic notices a user receives and how.</summary>
public static class UserNoticePreferencesViewText
{
    /// <summary>The section's title.</summary>
    public static readonly TextKey Title = new("team.userNotice.preferences.title", "Notices");

    /// <summary>One sentence above the list.</summary>
    public static readonly TextKey Explanation = new("team.userNotice.preferences.explanation", "Choose which notices you receive and how. A change is saved at once.");

    /// <summary>Under a notice the user has not chosen for, when their teams' defaults differ.</summary>
    public static readonly TextKey VariesByTeam = new("team.userNotice.preferences.variesByTeam", "Your teams have different defaults for this until you choose.");

    /// <summary>Under a notice whose mail category the user turned off; <c>{0}</c> is the category's name.</summary>
    public static readonly TextKey CategoryOff = new("team.userNotice.preferences.categoryOff", "Not sent while {0} is turned off in the mail you receive.");

    /// <summary>When the user may receive no notice at all.</summary>
    public static readonly TextKey Empty = new("team.userNotice.preferences.empty", "There are no notices for you.");

    /// <summary>When a change could not be saved.</summary>
    public static readonly TextKey SaveFailed = new("team.userNotice.preferences.saveFailed", "Your choice could not be saved.");

    /// <summary>When the reader may not see or change this user's choices.</summary>
    public static readonly TextKey NotPermitted = new("team.userNotice.preferences.notPermitted", "You cannot see or change this user's notice choices.");

    /// <summary>When nobody is signed in and no user was named.</summary>
    public static readonly TextKey NoUser = new("team.userNotice.preferences.noUser", "Sign in to choose which notices you receive.");

    /// <summary>Every key the component renders, its own and the shared delivery names, for building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Title, Explanation, VariesByTeam, CategoryOff, Empty, SaveFailed, NotPermitted, NoUser, UserNoticeText.AtOnce, UserNoticeText.Digest, UserNoticeText.Delivery];
}
