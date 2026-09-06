namespace Tharga.Team;

/// <summary>
/// The scope-checked entry point for team operations — <b>the interface a component, controller or MCP
/// provider should inject</b>. Every member carries a <c>[RequireScope]</c> attribute enforced by
/// <c>ScopeProxy</c>, so a caller lacking the scope is refused before the operation runs.
/// </summary>
/// <remarks>
/// <see cref="ITeamService"/> is the internal path beneath this one: it is the contract a host implements,
/// and its reads are deliberately unchecked so that framework code — building claims, revalidating a
/// circuit — can read without needing the very scopes it is in the middle of computing. Calling it from a
/// first-level surface bypasses authorization entirely, which is why the read methods below exist.
/// </remarks>
public interface ITeamManagementService
{
    [RequireScope(TeamScopes.Manage)]
    Task RenameTeamAsync(string teamKey, string name);

    /// <summary>
    /// Delete a team. Requires being its <b>Owner</b> (with <c>AllowTeamCreation</c>), or the
    /// <c>teams:delete</c> system scope.
    /// </summary>
    /// <remarks>
    /// <b>From 3.20.3, <c>team:manage</c> on the team is no longer sufficient.</b> That scope is registered
    /// at <see cref="AccessLevel.Administrator"/>, so it admitted any administrator — while the UI had
    /// always offered Delete to the Owner alone. The service now agrees with the button.
    /// <para>
    /// The attribute below is the team-bound half of the signature and does not state the whole rule; the
    /// Owner check lives in <c>AuthorizationTeamServiceDecorator</c>, because no scope can express it —
    /// every registered scope is granted to Administrator as well.
    /// </para>
    /// <para>
    /// The delete is recoverable by default; <see cref="ITeamService.PurgeTeamAsync{TMember}"/> is the
    /// irreversible one and needs <c>teams:purge</c>.
    /// </para>
    /// </remarks>
    [RequireScope(TeamScopes.Manage)]
    Task DeleteTeamAsync(string teamKey);

    [RequireScope(TeamScopes.MemberManage)]
    Task AddMemberAsync(string teamKey, InviteUserModel model);

    [RequireScope(TeamScopes.MemberManage)]
    Task RemoveMemberAsync(string teamKey, string userKey);

    [RequireScope(TeamScopes.MemberManage)]
    Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel);

    /// <summary>
    /// Suspends a member's access to the team, or restores it. The member keeps their membership, access
    /// level, roles and history, and still sees the team in the selector — they are simply granted no
    /// team scopes, so every scoped operation refuses.
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="TeamScopes.MemberManage"/>, which already authorizes <see cref="RemoveMemberAsync"/> —
    /// strictly more destructive, so a separate grant would guard the lesser act more carefully than the
    /// greater one.
    /// <para>
    /// The Owner cannot be suspended, and a member cannot suspend themselves. Both are refused by the
    /// service, not merely hidden in the UI.
    /// </para>
    /// <para>
    /// Distinct from <c>IUserManagementService.SetUserDisabledAsync</c>, which blocks a person from the
    /// whole application. This one is bounded to a single team.
    /// </para>
    /// </remarks>
    [RequireScope(TeamScopes.MemberManage)]
    Task SetMemberSuspendedAsync(string teamKey, string userKey, bool suspended);

    /// <summary>
    /// Gives an outstanding invitation a fresh lifetime, <b>keeping its code</b>.
    /// </summary>
    /// <remarks>
    /// <b>The point is what it does not do: mint a new code.</b> A link that has already been mailed keeps
    /// working, so extending an invitation costs the recipient nothing and needs no second message. That is
    /// only possible because the expiry lives on the invitation record rather than being derived from its
    /// creation time — see <see cref="Invitation.ExpiresAt"/>.
    /// <para>
    /// An operation rather than a settable expiry, so it can be authorized and audited as one fact.
    /// </para>
    /// <para>
    /// The new expiry is <c>now</c> plus the configured <see cref="InvitationOptions.Lifetime"/>. Where no
    /// lifetime is configured invitations do not expire, and extending clears any expiry the invitation was
    /// carrying.
    /// </para>
    /// </remarks>
    [RequireScope(TeamScopes.MemberManage)]
    Task ExtendInvitationAsync(string teamKey, string inviteKey);

    [RequireScope(TeamScopes.MemberManage)]
    Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles);

    [RequireScope(TeamScopes.MemberManage)]
    Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides);

    [RequireScope(TeamScopes.MemberManage)]
    Task SetMemberNameAsync(string teamKey, string userKey, string name);

    [RequireScope(TeamScopes.Manage)]
    Task TransferOwnershipAsync(string teamKey, string newOwnerUserKey);

    [RequireScope(TeamScopes.Manage)]
    Task SetTeamIconAsync(string teamKey, byte[] data, string contentType);

    [RequireScope(TeamScopes.Manage)]
    Task ClearTeamIconAsync(string teamKey);

    /// <summary>
    /// Replace the team's runtime-defined custom roles. Requires <c>team:manage</c> on the team. Each
    /// role's scopes must be app-registered scopes (rejected otherwise, as a privilege-escalation guard).
    /// Assigning these roles to members remains a <c>member:manage</c> operation.
    /// </summary>
    [RequireScope(TeamScopes.Manage)]
    Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles);

    [RequireScope(TeamScopes.Read)]
    Task SetMemberLastSeenAsync(string teamKey);

    [RequireScope(TeamScopes.Read)]
    Task SetInvitationResponseAsync(string teamKey, string userKey, string inviteCode, bool accept);

    /// <summary>What the team exposes to an oversight caller. Requires <c>team:manage</c> on that team.</summary>
    /// <remarks>
    /// Consent is a team's own statement about what it exposes inbound, so it is deliberately gated by
    /// the in-team manage scope rather than by any system grant — an operator overriding it would be a
    /// much larger claim than fixing a typo in a name.
    /// </remarks>
    [RequireScope(TeamScopes.Manage)]
    Task SetTeamConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null);

    /// <summary>
    /// Makes an existing member the <b>sole owner</b> of the team, demoting every other owner to
    /// <see cref="AccessLevel.Administrator"/>. Requires the <see cref="SystemTeamScopes.SetOwner"/> system
    /// scope. Returns the user keys of the owners demoted, empty when nothing changed.
    /// </summary>
    /// <remarks>
    /// The scope is a <i>system</i> grant, unlike everything else here, for two reasons rather than one. On
    /// an ownerless team no in-team caller can exist. On a team that has an owner, the in-team caller who
    /// should move ownership <i>is</i> the owner, and they already have
    /// <c>ITeamService.TransferOwnershipAsync</c> — admitting an in-team fallback here would let an
    /// Administrator depose the owner, which <c>SetMemberRoleAsync</c> exists to refuse.
    /// <para>
    /// Enforcement lives in <c>AuthorizationTeamServiceDecorator</c>; the attribute below documents the
    /// team-bound half of the signature and does not describe the whole rule.
    /// </para>
    /// </remarks>
    [RequireScope(SystemTeamScopes.SetOwner)]
    Task<SetOwnerResult> SetOwnerAsync(string teamKey, string newOwnerUserKey);

    /// <summary>One team and its members. Requires <c>team:read</c> on that team.</summary>
    [RequireScope(TeamScopes.Read)]
    Task<ITeam<TMember>> GetTeamAsync<TMember>(string teamKey) where TMember : ITeamMember;

    /// <summary>Team metadata without the roster. Requires <c>team:read</c> on that team.</summary>
    [RequireScope(TeamScopes.Read)]
    Task<ITeam> GetTeamByKeyAsync(string teamKey);

    /// <summary>The team's members. Requires <c>team:read</c> on that team.</summary>
    [RequireScope(TeamScopes.Read)]
    IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey);

    /// <summary>The team's runtime-defined custom roles. Requires <c>team:read</c> on that team.</summary>
    /// <remarks>
    /// A read of team detail, so it is gated like the others — not like its write sibling
    /// <see cref="SetTeamCustomRolesAsync"/>, which needs <c>team:manage</c>. Seeing which roles a team
    /// defines is part of seeing the team.
    /// </remarks>
    [RequireScope(TeamScopes.Read)]
    Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey);

    /// <summary>
    /// One <b>active</b> member of a team. Requires <c>team:read</c> on that team.
    /// </summary>
    /// <remarks>
    /// <b>Whether an invited or rejected member comes back here is up to the host's store, so do not
    /// rely on either answer.</b> This resolves through the store's "teams this user belongs to" query.
    /// The MongoDB store filters that query on <c>State == MembershipState.Member</c>, which makes a
    /// pending invitee indistinguishable from somebody who was never in the team; a store written
    /// differently may well return them.
    /// <para>
    /// So treat a non-null result as "has some membership" and null as "cannot act as a member" — never
    /// as a reliable answer to <i>which</i> state they are in. Anything that must tell the states apart —
    /// a message explaining a refusal, a roster count, an admin grid — has to use
    /// <see cref="GetMembersAsync"/>, which reads the team directly and is the only portable way to see
    /// every state.
    /// </para>
    /// <para>
    /// Not hypothetical: suspending a member shipped with this bug, refusing an invitee with "is not a
    /// member of team", which was both untrue and unhelpful.
    /// </para>
    /// </remarks>
    [RequireScope(TeamScopes.Read)]
    Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey);
}
