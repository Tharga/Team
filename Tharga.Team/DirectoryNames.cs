namespace Tharga.Team;

/// <summary>
/// Rules for comparing a local user's name with the display name an external directory holds.
/// </summary>
public static class DirectoryNames
{
    /// <summary>
    /// The display name Entra External ID gives an account whose sign-up flow collected none.
    /// </summary>
    public const string Placeholder = "unknown";

    /// <summary>
    /// True when <paramref name="name"/> is not a real name: empty, or the directory's placeholder in any case.
    /// A placeholder is never taken into this application.
    /// </summary>
    public static bool IsPlaceholder(string name)
        => string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), Placeholder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the local name and the directory name disagree and at least one of them is a real name —
    /// two placeholders have nothing to move in either direction.
    /// </summary>
    public static bool Differ(string localName, string directoryName)
    {
        var local = IsPlaceholder(localName) ? null : localName.Trim();
        var directory = IsPlaceholder(directoryName) ? null : directoryName.Trim();
        if (local == null && directory == null) return false;

        return !string.Equals(local, directory, StringComparison.Ordinal);
    }
}
