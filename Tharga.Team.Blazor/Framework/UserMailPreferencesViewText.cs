namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserMailPreferencesView</c> — which categories of mail a user receives.</summary>
public static class UserMailPreferencesViewText
{
    /// <summary>The section's title.</summary>
    public static readonly TextKey Title = new("team.userMail.preferences.title", "Mail I receive");

    /// <summary>One sentence above the list.</summary>
    public static readonly TextKey Explanation = new("team.userMail.preferences.explanation", "Choose which mail you want. A change is saved at once.");

    /// <summary>When a change could not be saved.</summary>
    public static readonly TextKey SaveFailed = new("team.userMail.preferences.saveFailed", "Your choice could not be saved.");

    /// <summary>When the reader may not see or change this user's choices.</summary>
    public static readonly TextKey NotPermitted = new("team.userMail.preferences.notPermitted", "You cannot see or change this user's mail choices.");

    /// <summary>When nobody is signed in and no user was named.</summary>
    public static readonly TextKey NoUser = new("team.userMail.preferences.noUser", "Sign in to choose which mail you receive.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>; a required category's line is <see cref="UserMailText.AlwaysSent"/>, shared with the unsubscribe page.</summary>
    public static readonly TextKey[] All = [Title, Explanation, SaveFailed, NotPermitted, NoUser];
}
