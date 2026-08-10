namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>DeleteUserDialog</c>.</summary>
/// <remarks>
/// <b>This dialog is why plural handling had to be decided before #204 could finish.</b> Its ownership
/// warning composed one paragraph from three runtime choices — <i>"a team"</i> vs <i>"{n} teams"</i>,
/// <i>"it"</i> vs <i>"them"</i>, <i>"this team is"</i> vs <i>"these teams are"</i> — interleaved with fixed
/// text. That reads correctly in English and cannot be translated: the clauses reorder, and in several
/// languages the count changes agreement elsewhere in the sentence.
/// <para>
/// <b>Each plural form is one key holding a whole sentence</b>, following what 3.10.9 did for the two
/// dialogs that composed a head and a tail. The consequence, stated because it is a decision and not an
/// oversight: a language with more than two plural categories (Polish, Russian, Arabic) cannot be
/// expressed. English and Swedish both have two. Changing it later means an additive overload on
/// <see cref="IThargaTextProvider"/> that takes a count, which stays possible precisely because no key
/// here is a fragment.
/// </para>
/// <para>
/// <b>Both variants of a pair take the same arguments in the same order</b>, even where one does not use
/// all of them — <see cref="OwnsOneTeam"/> ignores <c>{1}</c>. A caller then passes one argument list
/// regardless of which form it picked, so choosing the wrong variant cannot silently shift a placeholder.
/// </para>
/// <para>
/// Inline emphasis is not carried in the values. The sentences previously wrapped the user name in
/// <c>&lt;b&gt;</c> and the scope in <c>&lt;code&gt;</c>; a translatable sentence is one string, and the
/// house precedent — <c>AssignOwnerDialog</c>, <c>TeamDialog</c> — is plain text with positional
/// placeholders. Wording is unchanged.
/// </para>
/// </remarks>
public static class DeleteUserDialogText
{
    /// <summary>The opening question. <c>{0}</c> is the user's display name.</summary>
    public static readonly TextKey Intro = new("team.deleteUser.intro",
        "Delete {0}? The user is removed from every team and the user record is deleted. This cannot be undone.");

    /// <summary>
    /// Ownership warning, singular. <c>{0}</c> user name, <c>{1}</c> team count (unused), <c>{2}</c> the
    /// scope that can reassign ownership.
    /// </summary>
    public static readonly TextKey OwnsOneTeam = new("team.deleteUser.ownsOneTeam",
        "{0} is the owner of a team. Deleting them leaves it with no owner, and ownership cannot be transferred afterwards — only an administrator holding {2} can assign a new one.");

    /// <summary>
    /// Ownership warning, plural. <c>{0}</c> user name, <c>{1}</c> team count, <c>{2}</c> the scope that
    /// can reassign ownership.
    /// </summary>
    public static readonly TextKey OwnsManyTeams = new("team.deleteUser.ownsManyTeams",
        "{0} is the owner of {1} teams. Deleting them leaves them with no owner, and ownership cannot be transferred afterwards — only an administrator holding {2} can assign a new one.");

    public static readonly TextKey TransferOneFirst = new("team.deleteUser.transferOneFirst",
        "Transfer ownership first if this team is still in use.");

    public static readonly TextKey TransferManyFirst = new("team.deleteUser.transferManyFirst",
        "Transfer ownership first if these teams are still in use.");

    public static readonly TextKey CheckingDirectory = new("team.deleteUser.checkingDirectory", "Checking the directory…");

    public static readonly TextKey DirectoryNotFound = new("team.deleteUser.directoryNotFound",
        "This user no longer exists in the directory — the account has already been deleted there. Only the local user is removed.");

    public static readonly TextKey DirectoryNotLinked = new("team.deleteUser.directoryNotLinked",
        "This user could not be matched to a directory account, so there is nothing to delete in the directory. Only the local user is removed.");

    public static readonly TextKey DirectoryDisabled = new("team.deleteUser.directoryDisabled",
        "The user exists in the directory, but the account is disabled.");

    public static readonly TextKey DirectoryFound = new("team.deleteUser.directoryFound", "The user exists in the directory.");

    /// <summary><c>{0}</c> is the error detail from the directory.</summary>
    public static readonly TextKey DirectoryCheckFailed = new("team.deleteUser.directoryCheckFailed", "Directory check failed: {0}");

    public static readonly TextKey AlsoDeleteFromDirectory = new("team.deleteUser.alsoDeleteFromDirectory",
        "Also delete from the external directory");

    public static readonly TextKey DirectoryWarning = new("team.deleteUser.directoryWarning",
        "Deleting from the directory removes the account organization-wide, affecting every application it signs in to — not just this one. An administrator can restore it for a limited time (30 days in Microsoft Entra ID).");

    public static readonly TextKey Delete = new("team.deleteUser.delete", "Delete");

    public static readonly TextKey Cancel = new("team.deleteUser.cancel", "Cancel");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Intro, OwnsOneTeam, OwnsManyTeams, TransferOneFirst, TransferManyFirst, CheckingDirectory,
        DirectoryNotFound, DirectoryNotLinked, DirectoryDisabled, DirectoryFound, DirectoryCheckFailed,
        AlsoDeleteFromDirectory, DirectoryWarning, Delete, Cancel
    ];
}
