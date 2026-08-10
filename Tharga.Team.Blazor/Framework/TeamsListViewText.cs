namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>TeamsListView</c> — the Teams tab of <c>UsersView</c>.</summary>
/// <remarks>
/// Column titles are separate keys from the headings that repeat their words. <see cref="ColumnMembers"/>
/// and <see cref="MembersHeading"/> are both "Members" in English and need not be in another language —
/// one labels a count column, the other introduces a roster — and a shared key would force a translator to
/// pick one word for both.
/// </remarks>
public static class TeamsListViewText
{
    /// <summary><c>{0}</c> is the required system scope.</summary>
    public static readonly TextKey RequiresScope = new("team.teamsList.requiresScope",
        "Viewing teams and their members requires the {0} system scope.");

    public static readonly TextKey ColumnName = new("team.teamsList.columnName", "Name");
    public static readonly TextKey BadgeEmpty = new("team.teamsList.badgeEmpty", "Empty");
    public static readonly TextKey ColumnOwner = new("team.teamsList.columnOwner", "Owner");
    public static readonly TextKey BadgeNoOwner = new("team.teamsList.badgeNoOwner", "No owner");
    public static readonly TextKey ColumnMembers = new("team.teamsList.columnMembers", "Members");

    /// <summary><c>{0}</c> is the number of invited-but-not-joined members.</summary>
    public static readonly TextKey InvitedCount = new("team.teamsList.invitedCount", "+{0} invited");

    public static readonly TextKey ColumnLastUsed = new("team.teamsList.columnLastUsed", "Last used");
    public static readonly TextKey ColumnConsent = new("team.teamsList.columnConsent", "Consent");

    public static readonly TextKey ActionView = new("team.teamsList.actionView", "View");
    public static readonly TextKey ActionAuditLog = new("team.teamsList.actionAuditLog", "Audit log");
    public static readonly TextKey ActionRename = new("team.teamsList.actionRename", "Rename");
    public static readonly TextKey ActionSetIcon = new("team.teamsList.actionSetIcon", "Set icon");
    public static readonly TextKey ActionAssignOwner = new("team.teamsList.actionAssignOwner", "Assign owner");
    public static readonly TextKey ActionDelete = new("team.teamsList.actionDelete", "Delete");

    public static readonly TextKey TeamKey = new("team.teamsList.teamKey", "Team key");
    public static readonly TextKey CopyTeamKey = new("team.teamsList.copyTeamKey", "Copy team key");
    public static readonly TextKey MembersHeading = new("team.teamsList.membersHeading", "Members");
    public static readonly TextKey ShowThisUser = new("team.teamsList.showThisUser", "Show this user");
    public static readonly TextKey ColumnEmail = new("team.teamsList.columnEmail", "Email");
    public static readonly TextKey ColumnRole = new("team.teamsList.columnRole", "Role");
    public static readonly TextKey ColumnState = new("team.teamsList.columnState", "State");
    public static readonly TextKey ColumnLastSeen = new("team.teamsList.columnLastSeen", "Last seen");

    /// <summary>Dialog titles. <c>{0}</c> is the team name in each.</summary>
    public static readonly TextKey AuditLogTitle = new("team.teamsList.auditLogTitle", "Audit log — {0}");
    public static readonly TextKey RenameTitle = new("team.teamsList.renameTitle", "Rename {0}");
    public static readonly TextKey IconTitle = new("team.teamsList.iconTitle", "Icon — {0}");
    public static readonly TextKey AssignOwnerTitle = new("team.teamsList.assignOwnerTitle", "Assign owner — {0}");

    public static readonly TextKey OwnerAssigned = new("team.teamsList.ownerAssigned", "Owner assigned");

    /// <summary><c>{0}</c> is the team name.</summary>
    public static readonly TextKey OwnerAssignedDetail = new("team.teamsList.ownerAssignedDetail", "'{0}' now has an owner.");

    public static readonly TextKey CouldNotAssignOwner = new("team.teamsList.couldNotAssignOwner", "Could not assign owner");

    /// <summary>
    /// Delete confirmation, singular. <c>{0}</c> team name, <c>{1}</c> member count (unused).
    /// </summary>
    /// <remarks>
    /// The member count was previously composed into this sentence at runtime — <c>"1 member"</c> or
    /// <c>"{n} members"</c> — which cannot be translated, so each form is a whole sentence. Both variants
    /// take the same arguments in the same order so a caller passes one list regardless of which it picks.
    /// </remarks>
    public static readonly TextKey DeleteConfirmOne = new("team.teamsList.deleteConfirmOne",
        "Team '{0}' and its 1 member will be permanently deleted. This cannot be undone.");

    /// <summary>Delete confirmation, plural. <c>{0}</c> team name, <c>{1}</c> member count.</summary>
    public static readonly TextKey DeleteConfirmMany = new("team.teamsList.deleteConfirmMany",
        "Team '{0}' and its {1} members will be permanently deleted. This cannot be undone.");

    public static readonly TextKey DeleteTitle = new("team.teamsList.deleteTitle", "Delete team");
    public static readonly TextKey DeleteOk = new("team.teamsList.deleteOk", "Delete");
    public static readonly TextKey DeleteCancel = new("team.teamsList.deleteCancel", "Cancel");
    public static readonly TextKey TeamDeleted = new("team.teamsList.teamDeleted", "Team deleted");
    public static readonly TextKey DeleteFailed = new("team.teamsList.deleteFailed", "Delete failed");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        RequiresScope, ColumnName, BadgeEmpty, ColumnOwner, BadgeNoOwner, ColumnMembers, InvitedCount,
        ColumnLastUsed, ColumnConsent, ActionView, ActionAuditLog, ActionRename, ActionSetIcon,
        ActionAssignOwner, ActionDelete, TeamKey, CopyTeamKey, MembersHeading, ShowThisUser, ColumnEmail,
        ColumnRole, ColumnState, ColumnLastSeen, AuditLogTitle, RenameTitle, IconTitle, AssignOwnerTitle,
        OwnerAssigned, OwnerAssignedDetail, CouldNotAssignOwner, DeleteConfirmOne, DeleteConfirmMany,
        DeleteTitle, DeleteOk, DeleteCancel, TeamDeleted, DeleteFailed
    ];
}
