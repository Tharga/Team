namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// What saving the rename dialog will do, worked out from what it shows. Taking the directory's name and writing
/// to the directory are the two changes that reach past this application's own copy, so they are the two that
/// ask before they happen.
/// </summary>
/// <param name="Current">The name held here now, trimmed, or null.</param>
/// <param name="NewName">The name entered, trimmed, or null when it is left empty.</param>
/// <param name="DirectoryName">The name the directory holds, trimmed, or null when it holds none or was not read.</param>
/// <param name="TakeFromDirectory">The name here becomes the directory's name.</param>
/// <param name="WriteToDirectory">The entered name is written to the directory.</param>
internal sealed record UserNameChange(string Current, string NewName, string DirectoryName, bool TakeFromDirectory, bool WriteToDirectory)
{
    /// <summary>The name held here changes.</summary>
    public bool RenamesHere => !string.Equals(Current, NewName, StringComparison.Ordinal);

    /// <summary>Saving would change nothing, here or in the directory.</summary>
    public bool IsNothing => !RenamesHere && !WriteToDirectory;

    /// <summary>The change involves the directory, so it is confirmed first.</summary>
    public bool NeedsConfirmation => TakeFromDirectory || WriteToDirectory;

    /// <summary>
    /// The change saving would make. Entering exactly the directory's name is taking it, however it got into the
    /// field. Writing happens only when asked, and only when the entered name is real and differs from the directory's.
    /// </summary>
    public static UserNameChange From(string currentName, string enteredName, string directoryName, bool writeToDirectory)
    {
        var current = Trimmed(currentName);
        var name = Trimmed(enteredName);
        var directory = Trimmed(directoryName);
        var realDirectoryName = DirectoryNames.IsPlaceholder(directory) ? null : directory;

        var take = realDirectoryName != null && name == realDirectoryName && name != current;
        var write = writeToDirectory && !take && !DirectoryNames.IsPlaceholder(name) && DirectoryNames.Differ(name, directory);

        return new UserNameChange(current, name, directory, take, write);
    }

    private static string Trimmed(string name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();
}
