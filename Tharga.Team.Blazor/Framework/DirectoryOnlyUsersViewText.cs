namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>DirectoryOnlyUsersView</c> — the tab listing directory accounts with
/// no local user record.
/// </summary>
public static class DirectoryOnlyUsersViewText
{
    public static readonly TextKey Load = new("team.directoryOnly.load", "Load");
    public static readonly TextKey Refresh = new("team.directoryOnly.refresh", "Refresh");

    /// <summary>Placeholder: the number found.</summary>
    public static readonly TextKey FoundCount = new("team.directoryOnly.foundCount", "{0} user(s) found only in the directory");

    public static readonly TextKey ColumnName = new("team.directoryOnly.columnName", "Name");
    public static readonly TextKey ColumnEmail = new("team.directoryOnly.columnEmail", "Email");
    public static readonly TextKey ColumnStatus = new("team.directoryOnly.columnStatus", "Status");
    public static readonly TextKey StatusDisabled = new("team.directoryOnly.statusDisabled", "Disabled");
    public static readonly TextKey StatusEnabled = new("team.directoryOnly.statusEnabled", "Enabled");

    public static readonly TextKey EmptyPrompt = new("team.directoryOnly.emptyPrompt",
        "Users that exist in the external directory but not in this application. Nothing is fetched automatically — press Load to query the directory.");

    public static readonly TextKey NotifyFailed = new("team.directoryOnly.notifyFailed", "Directory listing failed");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Load, Refresh, FoundCount, ColumnName, ColumnEmail, ColumnStatus,
        StatusDisabled, StatusEnabled, EmptyPrompt, NotifyFailed,
    ];
}
