namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// Gating decisions for the user administration surface. Viewing the admin lists and acting on users
/// (verify, delete) require the <c>users:manage</c> system scope — the service layer enforces the same
/// rule, so this gate is about rendering a friendly message instead of an exception. Directory features
/// additionally require a registered <see cref="IUserDirectoryService"/> — without one they are hidden
/// entirely, not disabled.
/// </summary>
public static class UserAdminGate
{
    public static bool CanAdministerUsers(bool hasUsersManageScope)
        => hasUsersManageScope;

    /// <summary>
    /// Whether the directory features (verify, badge column, directory-only tab, the directory delete
    /// opt-in) are offered at all.
    /// </summary>
    /// <param name="hasUsersManageScope">Whether the caller holds <c>users:manage</c>.</param>
    /// <param name="directoryRegistered">
    /// Whether a directory is registered <i>and reports itself configured</i>. A registered directory
    /// that is missing its credentials counts as absent here — offering Verify and then throwing on the
    /// first Graph call is the defect this closes, and it is the same shape as the buttons that threw
    /// when clicked before per-team action gating landed.
    /// </param>
    public static bool ShowDirectoryFeatures(bool hasUsersManageScope, bool directoryRegistered)
        => hasUsersManageScope && directoryRegistered;

    /// <summary>
    /// Whether the Teams tab offers deleting a team. Requires the <see cref="SystemTeamScopes.Delete"/>
    /// system scope, which authorizes deleting any team irrespective of membership.
    /// </summary>
    /// <remarks>
    /// Deliberately independent of consent and of the caller's access level on the team. Consent governs
    /// what a team exposes inbound; it does not decide who may destroy it. The scope must be a
    /// <i>system</i> grant — resolve it with <c>TeamScopeGate.HasSystemScope</c>, never a bare
    /// <c>HasClaim</c>, so an in-team grant of the same name cannot satisfy it.
    /// </remarks>
    public static bool CanDeleteTeams(bool hasTeamsDeleteScope)
        => hasTeamsDeleteScope;

    /// <summary>
    /// Whether the Teams tab offers restoring a soft-deleted team. The same grant as deleting one.
    /// </summary>
    /// <remarks>
    /// Restoring is strictly less destructive than the delete it undoes, so it needs no scope of its own —
    /// anyone trusted to remove a team is trusted to change their mind. Offered only on a team that is
    /// actually deleted; a control that does nothing is worse than no control.
    /// </remarks>
    public static bool CanRestoreTeam(bool hasTeamsDeleteScope, bool isDeleted)
        => hasTeamsDeleteScope && isDeleted;

    /// <summary>
    /// Whether the Teams tab offers permanently removing a soft-deleted team.
    /// </summary>
    /// <remarks>
    /// <b>Its own scope, and only on an already-deleted team.</b> Purge is the one irreversible team
    /// operation and the only one needing the deployment's privilege to destroy stored data, so it is
    /// gated on <c>teams:purge</c> rather than on <c>teams:delete</c> (Tharga/Team#224).
    /// <para>
    /// Requiring the team to be soft-deleted first is deliberate: it makes destruction a second, separate
    /// decision rather than something reachable in one click from a live team.
    /// </para>
    /// </remarks>
    public static bool CanPurgeTeam(bool hasTeamsPurgeScope, bool isDeleted)
        => hasTeamsPurgeScope && isDeleted;

    /// <summary>
    /// Whether the Users tab offers deleting the user on this row. False for the signed-in caller's own
    /// row: deleting yourself drops your user record and, through
    /// <c>ITeamService.RemoveUserFromAllTeamsAsync</c>, your membership of every team — while your session
    /// continues holding claims that no longer correspond to anything.
    /// </summary>
    /// <remarks>
    /// An administrator who genuinely should go needs another administrator to remove them, which also
    /// guarantees somebody is left holding <c>users:manage</c>. This is the same class of guard as
    /// refusing to demote a sitting owner, and it is the most likely route to the ownerless-team state
    /// that <c>TeamServiceBase.SetMemberRoleAsync</c> exists to prevent — the sole owner of a team is
    /// very often the same person administering users.
    /// <para>
    /// <b>Fails closed when identity is unknown.</b> A missing key on either side returns false rather
    /// than true. The view already requires <c>users:manage</c>, so an authenticated caller always
    /// resolves; a null key means the caller could not be established, and that is not a state in which
    /// to offer an irreversible action on an account. The result is a visibly disabled control rather
    /// than a silently permitted mistake.
    /// </para>
    /// <para>
    /// Comparison is <see cref="StringComparison.OrdinalIgnoreCase"/>, deliberately unlike
    /// <c>MemberHighlight.IsCurrentMember</c>, which is case-sensitive. That one drives a highlight, where
    /// a false positive is cosmetic; this one guards a destructive action, where a false negative deletes
    /// an account. Where the two disagree the guard should be the stricter, so a key differing only in
    /// case is still treated as "you".
    /// </para>
    /// </remarks>
    /// <summary>
    /// Whether the Teams tab offers renaming a team or changing its icon. Requires the
    /// <see cref="SystemTeamScopes.Manage"/> system scope.
    /// </summary>
    /// <remarks>
    /// Presentational only. That scope deliberately does <b>not</b> reach consent or custom roles, so
    /// this gate must not be reused to offer either — the service refuses them, and a control that
    /// throws when clicked is the defect per-team action gating already had to fix once.
    /// <para>
    /// A <i>system</i> grant, resolved with <c>TeamScopeGate.HasSystemScope</c> — an in-team scope of the
    /// same name must not satisfy it, exactly as for <see cref="CanDeleteTeams(bool)"/>.
    /// </para>
    /// </remarks>
    public static bool CanManageTeams(bool hasTeamsManageScope)
        => hasTeamsManageScope;

    /// <summary>
    /// Whether the Teams tab offers the set-owner action. Requires the <b>system</b>
    /// <see cref="SystemTeamScopes.SetOwner"/> grant, and nothing else.
    /// </summary>
    /// <remarks>
    /// <b>No longer conditioned on the team being ownerless</b>, unlike the <c>teams:assign-owner</c> gate
    /// this replaces. The operation now serves three states — no owner, one owner, several — so gating on
    /// one of them would hide it from the two cases it was widened for.
    /// <para>
    /// A <i>system</i> grant, resolved with <c>TeamScopeGate.HasSystemScope</c>: an in-team scope of the
    /// same name must not satisfy it. The label the surface shows still varies by owner count, but that is
    /// wording, not authorization — see <c>TeamsListView</c>.
    /// </para>
    /// </remarks>
    public static bool CanSetOwner(bool hasSetOwnerScope)
        => hasSetOwnerScope;

    /// <summary>
    /// Whether the users list offers the per-row audit action. Requires the <b>system</b>
    /// <c>audit:read</c> grant.
    /// </summary>
    /// <remarks>
    /// System, not team: this list spans every team, so the audit it opens is cross-team and a grant on
    /// one team does not cover it.
    /// <para>
    /// <b>The action used to appear whenever the host set <c>ShowAuditLogButton</c></b>, and the dialog it
    /// opened answered "Access denied" — shown-then-refused, the defect PR #126 fixed for per-team
    /// actions. <c>TeamComponent</c> already gated its equivalent through
    /// <c>TeamActionGate.CanReadMemberAudit</c>; this surface was simply never given the same treatment.
    /// </para>
    /// </remarks>
    public static bool CanReadAudit(bool showAuditLogButton, bool hasSystemAuditRead)
        => showAuditLogButton && hasSystemAuditRead;

    public static bool CanDeleteUser(string rowUserKey, string currentUserKey)
    {
        if (string.IsNullOrEmpty(rowUserKey) || string.IsNullOrEmpty(currentUserKey)) return false;
        return !string.Equals(rowUserKey, currentUserKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the row offers disabling the user. Same self-exclusion as <see cref="CanDeleteUser"/>:
    /// an administrator who disables themselves needs a second one to undo it.
    /// </summary>
    /// <remarks>
    /// A separate gate rather than a call to <see cref="CanDeleteUser"/>, though the rule is identical
    /// today — they answer different questions, and tying them together means a future change to one
    /// silently moves the other. <b>Enabling is not gated</b>: the self-case cannot arise, since a
    /// disabled user has no session from which to enable themselves.
    /// <para>
    /// The service refuses the self-case as well. This gate only stops the row offering an action that
    /// would throw — a host can dispatch to the service directly through <c>ActionItems</c>.
    /// </para>
    /// </remarks>
    public static bool CanDisableUser(string rowUserKey, string currentUserKey)
    {
        if (string.IsNullOrEmpty(rowUserKey) || string.IsNullOrEmpty(currentUserKey)) return false;
        return !string.Equals(rowUserKey, currentUserKey, StringComparison.OrdinalIgnoreCase);
    }
}
