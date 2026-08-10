namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UsersListView</c> — the Users tab of <c>UsersView</c>.</summary>
/// <remarks>
/// <b>Two messages here were assembled at runtime and are now whole sentences per variant.</b> The disabled
/// tooltip appended <c>" by {name}"</c> to a fixed head, and the delete summary appended
/// <c>" and the directory"</c> while writing the count as <c>"team(s)"</c>. Neither survives translation:
/// the clauses reorder, and <c>team(s)</c> is an English shorthand with no equivalent in a language that
/// inflects.
/// <para>
/// The delete summary has <b>two independent binary axes</b> — one team or several, directory deleted or
/// not — so it is four keys rather than two. Enumerating them is the price of each being a sentence a
/// translator can rewrite freely; composing two halves would reintroduce exactly the defect.
/// </para>
/// </remarks>
public static class UsersListViewText
{
    public static readonly TextKey VerifyAll = new("team.usersList.verifyAll", "Verify all");

    public static readonly TextKey ColumnName = new("team.usersList.columnName", "Name");
    public static readonly TextKey ColumnEmail = new("team.usersList.columnEmail", "Email");
    public static readonly TextKey ColumnTeams = new("team.usersList.columnTeams", "Teams");
    public static readonly TextKey ColumnLastSeen = new("team.usersList.columnLastSeen", "Last seen");
    public static readonly TextKey BadgeDisabled = new("team.usersList.badgeDisabled", "Disabled");

    public static readonly TextKey ActionView = new("team.usersList.actionView", "View");
    public static readonly TextKey ActionVerify = new("team.usersList.actionVerify", "Verify");
    public static readonly TextKey ActionRename = new("team.usersList.actionRename", "Rename");
    public static readonly TextKey ActionSetIcon = new("team.usersList.actionSetIcon", "Set icon");
    public static readonly TextKey ActionAuditLog = new("team.usersList.actionAuditLog", "Audit log");
    public static readonly TextKey ActionEnable = new("team.usersList.actionEnable", "Enable");
    public static readonly TextKey ActionDisable = new("team.usersList.actionDisable", "Disable");
    public static readonly TextKey ActionDelete = new("team.usersList.actionDelete", "Delete");

    /// <summary>Labels on the caller's own row, where the action is disabled.</summary>
    /// <remarks>
    /// A disabled control with no stated reason reads as a bug, and <c>RadzenSplitButtonItem</c> carries no
    /// tooltip — so the reason has to be the label. That makes these display text, not a suffix to bolt on.
    /// </remarks>
    public static readonly TextKey ActionDisableSelf = new("team.usersList.actionDisableSelf", "Disable (this is you)");
    public static readonly TextKey ActionDeleteSelf = new("team.usersList.actionDeleteSelf", "Delete (this is you)");

    public static readonly TextKey UserKey = new("team.usersList.userKey", "User key");
    public static readonly TextKey CopyUserKey = new("team.usersList.copyUserKey", "Copy user key");
    public static readonly TextKey Identity = new("team.usersList.identity", "Identity");
    public static readonly TextKey CopyIdentity = new("team.usersList.copyIdentity", "Copy identity");
    public static readonly TextKey DirectoryId = new("team.usersList.directoryId", "Directory id");
    public static readonly TextKey CopyDirectoryId = new("team.usersList.copyDirectoryId", "Copy directory id");

    public static readonly TextKey TeamMemberships = new("team.usersList.teamMemberships", "Team memberships");
    public static readonly TextKey ColumnTeam = new("team.usersList.columnTeam", "Team");
    public static readonly TextKey ShowThisTeam = new("team.usersList.showThisTeam", "Show this team");
    public static readonly TextKey ColumnRole = new("team.usersList.columnRole", "Role");
    public static readonly TextKey ColumnState = new("team.usersList.columnState", "State");

    /// <summary>Dialog titles. <c>{0}</c> identifies the user.</summary>
    public static readonly TextKey IconTitle = new("team.usersList.iconTitle", "Icon — {0}");
    public static readonly TextKey RenameTitle = new("team.usersList.renameTitle", "Rename — {0}");
    public static readonly TextKey AuditLogTitle = new("team.usersList.auditLogTitle", "Audit log — {0}");

    public static readonly TextKey RenameHelp = new("team.usersList.renameHelp",
        "This sets the user's name everywhere. Leave it empty to fall back to a name resolved from their email.");
    public static readonly TextKey NamePlaceholder = new("team.usersList.namePlaceholder", "Name");

    public static readonly TextKey Save = new("team.usersList.save", "Save");
    public static readonly TextKey Cancel = new("team.usersList.cancel", "Cancel");

    /// <summary>Notification detail after a disable/enable. <c>{0}</c> identifies the user.</summary>
    public static readonly TextKey UserDisabledDetail = new("team.usersList.userDisabledDetail",
        "{0} will be signed out within the claim-revalidation interval.");
    public static readonly TextKey UserEnabledDetail = new("team.usersList.userEnabledDetail",
        "{0} can sign in again.");

    public static readonly TextKey RenamedLocallyTitle = new("team.usersList.renamedLocallyTitle",
        "Renamed here, not in the directory");
    public static readonly TextKey RenameFailed = new("team.usersList.renameFailed", "Rename failed");
    public static readonly TextKey VerificationFailed = new("team.usersList.verificationFailed", "Verification failed");

    public static readonly TextKey CannotDisableSelfTitle = new("team.usersList.cannotDisableSelfTitle",
        "Cannot disable your own account");
    public static readonly TextKey CannotDisableSelfDetail = new("team.usersList.cannotDisableSelfDetail",
        "Another administrator has to do it, so somebody is always left able to manage users.");

    /// <summary><c>{0}</c> identifies the user.</summary>
    public static readonly TextKey ConfirmDisable = new("team.usersList.confirmDisable",
        "Disable {0}? They are signed out shortly and cannot sign in again, but keep their teams and history so this can be undone.");
    public static readonly TextKey ConfirmDisableTitle = new("team.usersList.confirmDisableTitle", "Confirm disable user");

    public static readonly TextKey UserDisabled = new("team.usersList.userDisabled", "User disabled");
    public static readonly TextKey UserEnabled = new("team.usersList.userEnabled", "User enabled");
    public static readonly TextKey CouldNotDisable = new("team.usersList.couldNotDisable", "Could not disable user");
    public static readonly TextKey CouldNotEnable = new("team.usersList.couldNotEnable", "Could not enable user");

    /// <summary>
    /// Disabled-state tooltip, without an actor. <c>{0}</c> timestamp, <c>{1}</c> actor (unused).
    /// </summary>
    public static readonly TextKey DisabledTooltip = new("team.usersList.disabledTooltip",
        "Disabled in this application {0}. Not the same as being disabled in the directory.");

    /// <summary>Disabled-state tooltip, naming who did it. <c>{0}</c> timestamp, <c>{1}</c> actor.</summary>
    public static readonly TextKey DisabledByTooltip = new("team.usersList.disabledByTooltip",
        "Disabled in this application {0} by {1}. Not the same as being disabled in the directory.");

    public static readonly TextKey CannotDeleteSelfTitle = new("team.usersList.cannotDeleteSelfTitle",
        "Cannot delete your own account");
    public static readonly TextKey CannotDeleteSelfDetail = new("team.usersList.cannotDeleteSelfDetail",
        "Another administrator has to remove you, so somebody is always left able to manage users.");

    public static readonly TextKey DeleteUserTitle = new("team.usersList.deleteUserTitle", "Delete user");
    public static readonly TextKey DeletedLocallyTitle = new("team.usersList.deletedLocallyTitle", "User deleted locally");

    /// <summary><c>{0}</c> is the directory error.</summary>
    public static readonly TextKey DirectoryDeleteIncomplete = new("team.usersList.directoryDeleteIncomplete",
        "Directory delete did not complete: {0}");

    public static readonly TextKey UserDeleted = new("team.usersList.userDeleted", "User deleted");
    public static readonly TextKey DeleteFailed = new("team.usersList.deleteFailed", "Delete failed");

    /// <summary>
    /// What the delete removed. <c>{0}</c> is the team count in every variant, so one argument list serves
    /// all four.
    /// </summary>
    public static readonly TextKey RemovedFromOneTeam = new("team.usersList.removedFromOneTeam", "Removed from 1 team.");
    public static readonly TextKey RemovedFromManyTeams = new("team.usersList.removedFromManyTeams", "Removed from {0} teams.");
    public static readonly TextKey RemovedFromOneTeamAndDirectory = new("team.usersList.removedFromOneTeamAndDirectory",
        "Removed from 1 team and the directory.");
    public static readonly TextKey RemovedFromManyTeamsAndDirectory = new("team.usersList.removedFromManyTeamsAndDirectory",
        "Removed from {0} teams and the directory.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        VerifyAll, ColumnName, ColumnEmail, ColumnTeams, ColumnLastSeen, BadgeDisabled, ActionView,
        ActionVerify, ActionRename, ActionSetIcon, ActionAuditLog, ActionEnable, ActionDisable, ActionDelete,
        ActionDisableSelf, ActionDeleteSelf, UserKey, CopyUserKey, Identity, CopyIdentity, DirectoryId,
        CopyDirectoryId, TeamMemberships, ColumnTeam, ShowThisTeam, ColumnRole, ColumnState, IconTitle,
        RenameTitle, AuditLogTitle, RenameHelp, NamePlaceholder, Save, Cancel, UserDisabledDetail,
        UserEnabledDetail, RenamedLocallyTitle, RenameFailed, VerificationFailed,
        CannotDisableSelfTitle, CannotDisableSelfDetail, ConfirmDisable, ConfirmDisableTitle, UserDisabled,
        UserEnabled, CouldNotDisable, CouldNotEnable, DisabledTooltip, DisabledByTooltip,
        CannotDeleteSelfTitle, CannotDeleteSelfDetail, DeleteUserTitle, DeletedLocallyTitle,
        DirectoryDeleteIncomplete, UserDeleted, DeleteFailed, RemovedFromOneTeam, RemovedFromManyTeams,
        RemovedFromOneTeamAndDirectory, RemovedFromManyTeamsAndDirectory
    ];
}
