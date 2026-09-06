namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// Visibility and enablement gates for the per-team action buttons rendered by
/// <c>TeamComponent</c>.
/// </summary>
/// <remarks>
/// The <c>team:manage</c> scope is emitted by <c>TeamServerClaimsTransformation</c> for the
/// currently-selected team only, so holding it authorizes actions on that team and no other.
/// Gating every team card on the bare scope flag offers buttons the server then rejects with
/// <see cref="UnauthorizedAccessException"/> (Tharga/Team#125).
/// </remarks>
internal static class TeamActionGate
{
    /// <summary>
    /// Whether the caller may manage <paramref name="teamKey"/>: the manage scope is held and that
    /// team is the selected one the scope was issued for.
    /// </summary>
    public static bool CanManage(bool hasManageScope, string selectedTeamKey, string teamKey)
        => hasManageScope && IsSelected(selectedTeamKey, teamKey);

    /// <summary>
    /// Whether <paramref name="teamKey"/> is the currently selected team. Every per-team action is
    /// confined to it: the scopes are issued for the selected team, and an action offered on another
    /// team card is one the caller cannot carry out there.
    /// </summary>
    private static bool IsSelected(string selectedTeamKey, string teamKey)
    {
        if (string.IsNullOrEmpty(selectedTeamKey) || string.IsNullOrEmpty(teamKey)) return false;
        return string.Equals(selectedTeamKey, teamKey, StringComparison.Ordinal);
    }

    /// <summary>Whether the Rename action should be visible.</summary>
    public static bool CanRename(bool hasManageScope, string selectedTeamKey, string teamKey)
        => CanManage(hasManageScope, selectedTeamKey, teamKey);

    /// <summary>
    /// Whether the per-member audit-history action should be visible on <paramref name="teamKey"/>.
    /// </summary>
    /// <remarks>
    /// Unlike the manage scopes, <c>audit:read</c> is meaningful at both levels and they mean different
    /// things: a <b>system</b> grant reads every team's log, so it is not confined to the selection,
    /// while a <b>team</b> grant is issued for the selected team only and must be. Collapsing the two
    /// would either hide the action from an oversight role or offer it where the server refuses —
    /// the same two failure modes <see cref="CanManage"/> exists to avoid.
    /// </remarks>
    public static bool CanReadMemberAudit(bool hasSystemAuditRead, bool hasTeamAuditRead, string selectedTeamKey, string teamKey)
        => hasSystemAuditRead || (hasTeamAuditRead && IsSelected(selectedTeamKey, teamKey));

    /// <summary>
    /// Whether member-management actions (e.g. Invite User) should be visible: the member-manage scope is
    /// held and that team is the selected one it was issued for. Like the manage scope, member:manage is
    /// emitted only for the selected team, so a global flag would offer the action on every card
    /// (Tharga/Team#134).
    /// </summary>
    public static bool CanManageMembers(bool hasMemberManageScope, string selectedTeamKey, string teamKey)
        => CanManage(hasMemberManageScope, selectedTeamKey, teamKey);

    /// <summary>
    /// Whether the Delete action should be visible: manage rights on this team, host-enabled team
    /// creation, and team ownership.
    /// </summary>
    public static bool CanDelete(bool hasManageScope, string selectedTeamKey, string teamKey, bool allowTeamCreation, bool isOwner)
        => CanManage(hasManageScope, selectedTeamKey, teamKey) && allowTeamCreation && isOwner;

    /// <summary>
    /// Whether the Leave action should be visible: the caller is a member of this team and does not own
    /// it. Non-members have nothing to leave, and the owner must transfer ownership instead.
    /// </summary>
    /// <remarks>
    /// <b>Not confined to the selected team, unlike every other action here.</b> The others are, because
    /// their scopes are issued for the selected team and offering them elsewhere means offering something
    /// the server refuses. Leaving carries no scope at all — <c>ITeamDirectoryService.LeaveTeamAsync</c>
    /// is authorized by naming no user but the caller — so the selection has nothing to do with it, and
    /// requiring it would make somebody select each of five teams in turn to leave them.
    /// </remarks>
    public static bool CanLeave(bool isMember, bool isOwner)
        => isMember && !isOwner;

    /// <summary>
    /// Whether the Transfer ownership action should be visible: on the selected team, where the caller
    /// owns it and there is somebody to hand it to.
    /// </summary>
    public static bool CanTransferOwnership(bool isOwner, bool hasOtherMembers, string selectedTeamKey, string teamKey)
        => isOwner && hasOtherMembers && IsSelected(selectedTeamKey, teamKey);

    /// <summary>
    /// Whether the consent selector should be editable: manage rights on this team and administrator
    /// level on it. It stays visible either way so the consented level can be read without being
    /// changeable. Access level alone is not sufficient — <c>SetTeamConsentAsync</c> is enforced on
    /// <c>team:manage</c>, which is issued for the selected team only, so gating on level alone offered
    /// an edit the service then rejected on every other team (Tharga/Team#140).
    /// </summary>
    public static bool CanEditConsent(bool hasManageScope, string selectedTeamKey, string teamKey, bool isAdministrator)
        => CanManage(hasManageScope, selectedTeamKey, teamKey) && isAdministrator;
}
