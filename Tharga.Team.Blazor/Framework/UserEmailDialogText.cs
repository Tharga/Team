namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UserEmailDialog</c> — an administrator setting a user's email address.</summary>
/// <remarks>Includes the two validation messages, for the reason <see cref="InviteUserDialogText"/> does.</remarks>
public static class UserEmailDialogText
{
    public static readonly TextKey Help = new("team.userEmail.help",
        "This sets the address the user is mailed at and invited by, and the one their avatar is looked up from. Nothing is sent to it.");

    /// <summary>Shown when the user is linked to a directory account.</summary>
    public static readonly TextKey DirectoryNote = new("team.userEmail.directoryNote",
        "The user is linked to a directory account. The directory keeps its own address; only this application changes.");

    public static readonly TextKey Email = new("team.userEmail.email", "Email address");

    public static readonly TextKey Required = new("team.userEmail.required", "An email address is required.");

    public static readonly TextKey Invalid = new("team.userEmail.invalid", "Enter a valid email address.");

    public static readonly TextKey Save = new("team.userEmail.save", "Save");

    public static readonly TextKey Cancel = new("team.userEmail.cancel", "Cancel");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Help, DirectoryNote, Email, Required, Invalid, Save, Cancel];
}
