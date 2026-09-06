namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>TeamComponent</c>.</summary>
/// <remarks>
/// The largest surface named in Tharga/Team#204, and the one the tenant noun appears on most — a host
/// calling the tenant an Organisation had this page say "Team" throughout.
/// <para>
/// <b>Two messages here were composed at runtime and are now one whole sentence per variant.</b> The
/// teamless message appended a "create your own" clause, and the suspended tooltip appended an optional
/// <c>" by {name}"</c>. Both reorder in other languages, so a sentence assembled from a head and a tail
/// cannot be translated — the same shape 3.10.9 resolved on two dialogs.
/// </para>
/// </remarks>
public static class TeamComponentText
{
    /// <summary>Grid paging summary. <c>{0}</c> first row, <c>{1}</c> last row, <c>{2}</c> total.</summary>
    public static readonly TextKey PagingSummary = new("team.component.pagingSummary", "Showing {0}-{1} of {2}");

    public static readonly TextKey ColumnTeam = new("team.component.columnTeam", "Team");
    public static readonly TextKey ColumnYourAccess = new("team.component.columnYourAccess", "Your access");
    public static readonly TextKey YourAccessTooltip = new("team.component.yourAccessTooltip", "Your access level on this team.");
    public static readonly TextKey ColumnConsent = new("team.component.columnConsent", "Consent");
    public static readonly TextKey ColumnMembers = new("team.component.columnMembers", "Members");

    /// <summary>Shown to a caller who belongs to no team, when creation is offered.</summary>
    public static readonly TextKey NoTeamsCanCreate = new("team.component.noTeamsCanCreate",
        "You are not member of a team. Create your own team and start inviting members.");

    /// <summary>The same state, when <c>AllowTeamCreation</c> is false.</summary>
    public static readonly TextKey NoTeams = new("team.component.noTeams", "You are not member of a team.");

    public static readonly TextKey CreateTeam = new("team.component.createTeam", "Create new Team");

    /// <summary>Dialog titles. <c>{0}</c> is the team name.</summary>
    public static readonly TextKey RenameTitle = new("team.component.renameTitle", "Rename {0}");
    public static readonly TextKey IconTitle = new("team.component.iconTitle", "Icon — {0}");

    public static readonly TextKey InviteUserTitle = new("team.component.inviteUserTitle", "Invite User");
    public static readonly TextKey InvitationSent = new("team.component.invitationSent", "Invitation sent");

    /// <summary><c>{0}</c> is the recipient address.</summary>
    public static readonly TextKey EmailSentTo = new("team.component.emailSentTo", "Email sent to {0}");

    public static readonly TextKey EmailFailed = new("team.component.emailFailed", "Email failed");

    /// <summary><c>{0}</c> is the error detail.</summary>
    public static readonly TextKey CouldNotSendEmail = new("team.component.couldNotSendEmail", "Could not send email: {0}");

    public static readonly TextKey InviteLinkCopied = new("team.component.inviteLinkCopied",
        "The invitation link has been copied to the clipboard.");

    /// <summary><c>{0}</c> is the member's email, <c>{1}</c> the team name.</summary>
    public static readonly TextKey ConfirmRemoveMember = new("team.component.confirmRemoveMember",
        "User '{0}' will be removed from the team '{1}'.");

    /// <summary><c>{0}</c> is the team name.</summary>
    public static readonly TextKey ConfirmLeaveTeam = new("team.component.confirmLeaveTeam",
        "You will be removed from the team '{0}'.");

    public static readonly TextKey MemberNotFound = new("team.component.memberNotFound", "Member not found");
    public static readonly TextKey MemberNotFoundDetail = new("team.component.memberNotFoundDetail",
        "The member could not be located. Please reload the page.");

    public static readonly TextKey NoEligibleMembers = new("team.component.noEligibleMembers", "No eligible members");
    public static readonly TextKey NoEligibleMembersDetail = new("team.component.noEligibleMembersDetail",
        "There are no other active members to transfer ownership to.");

    public static readonly TextKey TransferOwnershipTitle = new("team.component.transferOwnershipTitle", "Transfer Ownership");
    public static readonly TextKey Transfer = new("team.component.transfer", "Transfer");
    public static readonly TextKey Cancel = new("team.component.cancel", "Cancel");
    public static readonly TextKey OwnershipTransferred = new("team.component.ownershipTransferred", "Ownership transferred");

    public static readonly TextKey ColumnEmail = new("team.component.columnEmail", "EMail");
    public static readonly TextKey ColumnName = new("team.component.columnName", "Name");
    public static readonly TextKey ClearNameOverride = new("team.component.clearNameOverride",
        "Clear override (use the global user name)");

    /// <summary><c>{0}</c> is the user's global name.</summary>
    public static readonly TextKey NameOverrideTooltip = new("team.component.nameOverrideTooltip",
        "Team override. Original name: {0}");

    public static readonly TextKey EditDisplayName = new("team.component.editDisplayName", "Edit display name");
    public static readonly TextKey ColumnAccessLevel = new("team.component.columnAccessLevel", "Access Level");
    public static readonly TextKey ColumnRoles = new("team.component.columnRoles", "Roles");
    public static readonly TextKey ColumnScopeOverrides = new("team.component.columnScopeOverrides", "Scope Overrides");
    public static readonly TextKey ColumnScopes = new("team.component.columnScopes", "Scopes");
    public static readonly TextKey ColumnStatus = new("team.component.columnStatus", "Status");

    /// <summary><c>{0}</c> is the invitation timestamp.</summary>
    public static readonly TextKey InvitedAt = new("team.component.invitedAt", "Invited {0} UTC");

    public static readonly TextKey Suspended = new("team.component.suspended", "Suspended");
    public static readonly TextKey ColumnLastSeen = new("team.component.columnLastSeen", "Last Seen");

    public static readonly TextKey NotAMember = new("team.component.notAMember", "Not a member");
    public static readonly TextKey NotAMemberTooltip = new("team.component.notAMemberTooltip",
        "You are viewing this team through cross-team access, not membership.");
    public static readonly TextKey ConsentTooltip = new("team.component.consentTooltip",
        "What this team has consented to grant. Select the team to change it.");
    public static readonly TextKey NoAccess = new("team.component.noAccess", "No access");

    /// <summary>Label above the consent picker. <c>{0}</c> is the comma-separated list of consent roles.</summary>
    /// <remarks>
    /// The role list runs straight into the selected level so the two read as one sentence. Keeping the
    /// label a template rather than a prefix is what lets a translator put the list somewhere else.
    /// </remarks>
    public static readonly TextKey ConsentAccessFor = new("team.component.consentAccessFor", "Consent access for {0}");

    /// <summary><c>{0}</c> is the team name.</summary>
    public static readonly TextKey ConfirmDeleteTeam = new("team.component.confirmDeleteTeam", "Team '{0}' will be deleted.");

    public static readonly TextKey ConfirmSuspendTitle = new("team.component.confirmSuspendTitle", "Suspend access to this team?");
    public static readonly TextKey ConfirmSuspendDetail = new("team.component.confirmSuspendDetail",
        "They keep their membership, access level and history, and still see the team -- but can do nothing in it until access is restored.");

    public static readonly TextKey CouldNotSuspend = new("team.component.couldNotSuspend", "Could not suspend access");
    public static readonly TextKey CouldNotRestore = new("team.component.couldNotRestore", "Could not restore access");

    /// <summary>
    /// Suspended-member tooltip, without an actor. <c>{0}</c> timestamp, <c>{1}</c> actor (unused).
    /// </summary>
    public static readonly TextKey SuspendedTooltip = new("team.component.suspendedTooltip",
        "Access suspended {0}. Still a member of the team, with no scopes in it.");

    /// <summary>Suspended-member tooltip, naming who did it. <c>{0}</c> timestamp, <c>{1}</c> actor.</summary>
    public static readonly TextKey SuspendedByTooltip = new("team.component.suspendedByTooltip",
        "Access suspended {0} by {1}. Still a member of the team, with no scopes in it.");

    public static readonly TextKey ActionCopyInviteLink = new("team.component.actionCopyInviteLink", "Copy invitation link");
    public static readonly TextKey ActionAuditLog = new("team.component.actionAuditLog", "Audit log");
    public static readonly TextKey ActionRestoreAccess = new("team.component.actionRestoreAccess", "Restore access");
    public static readonly TextKey ActionSuspendAccess = new("team.component.actionSuspendAccess", "Suspend access");
    public static readonly TextKey ActionRemoveMember = new("team.component.actionRemoveMember", "Remove member");
    public static readonly TextKey ActionInviteUser = new("team.component.actionInviteUser", "Invite user");
    public static readonly TextKey ActionSetIcon = new("team.component.actionSetIcon", "Set icon");
    public static readonly TextKey ActionTransferOwnership = new("team.component.actionTransferOwnership", "Transfer ownership");
    public static readonly TextKey ActionLeaveTeam = new("team.component.actionLeaveTeam", "Leave");
    public static readonly TextKey ActionDeleteTeam = new("team.component.actionDeleteTeam", "Delete");
    public static readonly TextKey ActionRenameTeam = new("team.component.actionRenameTeam", "Rename");

    /// <summary><c>{0}</c> identifies the member.</summary>
    public static readonly TextKey AuditLogTitle = new("team.component.auditLogTitle", "Audit log — {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        PagingSummary, ColumnTeam, ColumnYourAccess, YourAccessTooltip, ColumnConsent, ColumnMembers,
        NoTeamsCanCreate, NoTeams, CreateTeam, RenameTitle, IconTitle, InviteUserTitle, InvitationSent,
        EmailSentTo, EmailFailed, CouldNotSendEmail, InviteLinkCopied, ConfirmRemoveMember, ConfirmLeaveTeam, MemberNotFound,
        MemberNotFoundDetail, NoEligibleMembers, NoEligibleMembersDetail, TransferOwnershipTitle, Transfer,
        Cancel, OwnershipTransferred, ColumnEmail, ColumnName, ClearNameOverride, NameOverrideTooltip,
        EditDisplayName, ColumnAccessLevel, ColumnRoles, ColumnScopeOverrides, ColumnScopes, ColumnStatus,
        InvitedAt, Suspended, ColumnLastSeen, NotAMember, NotAMemberTooltip, ConsentTooltip, NoAccess,
        ConsentAccessFor, ConfirmDeleteTeam, ConfirmSuspendTitle, ConfirmSuspendDetail, CouldNotSuspend,
        CouldNotRestore, SuspendedTooltip, SuspendedByTooltip, ActionCopyInviteLink, ActionAuditLog,
        ActionRestoreAccess, ActionSuspendAccess, ActionRemoveMember, ActionInviteUser, ActionSetIcon,
        ActionTransferOwnership, ActionLeaveTeam, ActionDeleteTeam, ActionRenameTeam, AuditLogTitle
    ];
}
