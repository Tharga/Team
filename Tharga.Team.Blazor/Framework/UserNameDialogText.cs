namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>UserNameDialog</c> — an administrator renaming a user, here and, when the
/// host allows it, in the directory.
/// </summary>
/// <remarks>
/// <see cref="Help"/> and <see cref="Name"/> keep the keys they had on <see cref="UsersListViewText"/> before the
/// rename moved into this dialog, so a host's existing translations of them still apply.
/// </remarks>
public static class UserNameDialogText
{
    public static readonly TextKey Help = new("team.usersList.renameHelp",
        "This sets the user's name everywhere. Leave it empty to fall back to a name resolved from their email.");

    public static readonly TextKey Name = new("team.usersList.namePlaceholder", "Name");

    public static readonly TextKey ReadingDirectory = new("team.userName.readingDirectory", "Reading the name in the directory…");

    /// <summary><c>{0}</c> is the name the directory holds.</summary>
    public static readonly TextKey InDirectory = new("team.userName.inDirectory", "In the directory: {0}");

    /// <summary>Stands in for a name that is empty, here or in the directory.</summary>
    public static readonly TextKey NoName = new("team.userName.noName", "(no name)");

    public static readonly TextKey NotInDirectory = new("team.userName.notInDirectory", "This user was not found in the directory.");

    /// <summary><c>{0}</c> is the directory error.</summary>
    public static readonly TextKey DirectoryReadFailed = new("team.userName.directoryReadFailed", "The directory could not be read: {0}");

    public static readonly TextKey UseThisName = new("team.userName.useThisName", "Use this name");

    public static readonly TextKey AlsoWriteToDirectory = new("team.userName.alsoWriteToDirectory", "Also update the name in the directory");

    public static readonly TextKey ConfirmHeading = new("team.userName.confirmHeading", "Confirm the change");

    /// <summary><c>{0}</c> is the name here now, <c>{1}</c> the new one.</summary>
    public static readonly TextKey NameHere = new("team.userName.nameHere", "Name here: {0} → {1}");

    /// <summary><c>{0}</c> is the name here now, <c>{1}</c> the directory's name it is replaced with.</summary>
    public static readonly TextKey NameHereFromDirectory = new("team.userName.nameHereFromDirectory", "Name here: {0} → {1} (from the directory)");

    /// <summary><c>{0}</c> is the name the directory holds now, <c>{1}</c> the one written to it.</summary>
    public static readonly TextKey NameInDirectory = new("team.userName.nameInDirectory", "Name in the directory: {0} → {1}");

    public static readonly TextKey DirectoryWriteWarning = new("team.userName.directoryWriteWarning",
        "This changes the user's account in the directory, not only in this application.");

    public static readonly TextKey Save = new("team.userName.save", "Save");

    public static readonly TextKey Ok = new("team.userName.ok", "OK");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Help, Name, ReadingDirectory, InDirectory, NoName, NotInDirectory, DirectoryReadFailed, UseThisName,
        AlsoWriteToDirectory, ConfirmHeading, NameHere, NameHereFromDirectory, NameInDirectory, DirectoryWriteWarning,
        Save, Ok
    ];
}
