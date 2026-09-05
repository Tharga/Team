using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Decorator over <see cref="ITeamService"/> that enforces the team-operation authorization model in the
/// service layer (so the same checks protect the Blazor circuit and any consumer's REST controller). Reads
/// the caller's claims via <see cref="TeamAuthorizer"/>:
/// <list type="bullet">
/// <item>Create — authenticated AND <c>AllowTeamCreation</c> (no scope; self-service).</item>
/// <item>Delete — (<c>team:manage</c> on the team AND <c>AllowTeamCreation</c>) OR <c>teams:delete</c> (system).</item>
/// <item>Rename / Consent — <c>team:manage</c> on the team.</item>
/// <item>Custom-role CRUD — the configurable custom-role manage scope on the team (default <c>team:manage</c>).</item>
/// <item>Member invite/remove/role/scope-overrides/display-name — <c>member:manage</c> on the team.</item>
/// <item>Remove user from all teams — <c>users:manage</c> (system; backs user deletion).</item>
/// <item>Transfer ownership — passed through (Owner-only is enforced by the inner service).</item>
/// </list>
/// Reads, consent-team lookup, last-seen touch, and invitation responses pass through (self-service / not gated here).
/// </summary>
public sealed class AuthorizationTeamServiceDecorator : ITeamService
{
    private readonly ITeamService _inner;
    private readonly TeamAuthorizer _authorizer;
    private readonly TeamPurgeCascade _purgeCascade;
    private readonly TeamLifecycleOptions _lifecycle;
    private readonly IScopeRegistry _scopeRegistry;
    private readonly ITenantRoleRegistry _tenantRoleRegistry;
    private readonly string _customRoleManageScope;

    public AuthorizationTeamServiceDecorator(ITeamService inner, TeamAuthorizer authorizer, TeamLifecycleOptions lifecycle, IScopeRegistry scopeRegistry = null, ITenantRoleRegistry tenantRoleRegistry = null, string customRoleManageScope = null, TeamPurgeCascade purgeCascade = null)
    {
        _inner = inner;
        _authorizer = authorizer;
        _purgeCascade = purgeCascade;
        _lifecycle = lifecycle;
        _scopeRegistry = scopeRegistry;
        _tenantRoleRegistry = tenantRoleRegistry;
        _customRoleManageScope = string.IsNullOrWhiteSpace(customRoleManageScope) ? TeamScopes.Manage : customRoleManageScope;
    }

    public event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent
    {
        add => _inner.TeamsListChangedEvent += value;
        remove => _inner.TeamsListChangedEvent -= value;
    }

    public event EventHandler<SelectTeamEventArgs> SelectTeamEvent
    {
        add => _inner.SelectTeamEvent += value;
        remove => _inner.SelectTeamEvent -= value;
    }

    // Reads & self-service — pass through.
    public IAsyncEnumerable<ITeam> GetTeamsAsync() => _inner.GetTeamsAsync();
    public IAsyncEnumerable<ITeam<TMember>> GetTeamsAsync<TMember>() where TMember : ITeamMember => _inner.GetTeamsAsync<TMember>();
    public Task<ITeam<TMember>> GetTeamAsync<TMember>(string teamKey) where TMember : ITeamMember => _inner.GetTeamAsync<TMember>(teamKey);
    public Task<ITeam> GetTeamByKeyAsync(string teamKey) => _inner.GetTeamByKeyAsync(teamKey);
    public Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey) => _inner.GetTeamMemberAsync(teamKey, userKey);
    public IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey) => _inner.GetMembersAsync(teamKey);
    public IAsyncEnumerable<ITeam> GetConsentedTeamsAsync(string[] userRoles) => _inner.GetConsentedTeamsAsync(userRoles);

    // Cross-team discovery — the only read gated here (teams:read). Enumeration is deliberately not audited.
    public async IAsyncEnumerable<ITeam> GetAllTeamsAsync()
    {
        await RequireAllTeamsReadAsync();
        await foreach (var team in _inner.GetAllTeamsAsync())
        {
            yield return team;
        }
    }

    public async IAsyncEnumerable<ITeam<TMember>> GetAllTeamsAsync<TMember>() where TMember : ITeamMember
    {
        await RequireAllTeamsReadAsync();
        await foreach (var team in _inner.GetAllTeamsAsync<TMember>())
        {
            yield return team;
        }
    }
    public Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey) => _inner.GetTeamCustomRolesAsync(teamKey);
    public Task SetMemberLastSeenAsync(string teamKey) => _inner.SetMemberLastSeenAsync(teamKey);
    public Task SetInvitationResponseAsync(string teamKey, string userKey, string inviteCode, bool accept) => _inner.SetInvitationResponseAsync(teamKey, userKey, inviteCode, accept);
    public Task TransferOwnershipAsync<TMember>(string teamKey, string newOwnerUserKey) where TMember : ITeamMember => _inner.TransferOwnershipAsync<TMember>(teamKey, newOwnerUserKey);

    /// <remarks>
    /// Gated on <c>users:manage</c>, the scope that already authorizes removing this user from every one
    /// of these teams. Requiring <c>teams:read</c> instead would hide the warning from exactly the caller
    /// about to cause the damage.
    /// </remarks>
    public async Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelAsync(string userKey, AccessLevel accessLevel)
    {
        if (!await _authorizer.HasSystemScopeAsync(SystemUserScopes.Manage))
            throw new UnauthorizedAccessException(
                $"Listing the teams a user holds '{accessLevel}' in requires the '{SystemUserScopes.Manage}' system scope.");

        return await _inner.GetTeamsForUserWithAccessLevelAsync(userKey, accessLevel);
    }

    /// <remarks>
    /// A <b>system</b> grant only — there is no in-team fallback, deliberately, and now for two reasons.
    /// On an ownerless team no in-team caller can exist. On a team that has an owner, the in-team caller
    /// who should move ownership <i>is</i> the owner, and <see cref="TransferOwnershipAsync{TMember}"/> is
    /// already their path — an in-team fallback here would let an Administrator depose the owner, which
    /// <c>SetMemberRoleAsync</c> exists to refuse.
    /// <para>
    /// An in-team scope of the same name must not satisfy it either, which is why this asks
    /// <c>HasSystemScopeAsync</c> rather than checking a claim.
    /// </para>
    /// </remarks>
    public async Task<SetOwnerResult> SetOwnerAsync<TMember>(string teamKey, string newOwnerUserKey) where TMember : ITeamMember
    {
        if (!await _authorizer.HasSystemScopeAsync(SystemTeamScopes.SetOwner))
            throw new UnauthorizedAccessException(
                $"Setting the owner of team '{teamKey}' requires the '{SystemTeamScopes.SetOwner}' system scope.");

        return await _inner.SetOwnerAsync<TMember>(teamKey, newOwnerUserKey);
    }

    // Lifecycle.
    public async Task<ITeam> CreateTeamAsync(string name)
    {
        await RequireCreateAsync();
        return await _inner.CreateTeamAsync(name);
    }

    public async Task DeleteTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        await RequireDeleteAsync(teamKey);
        await _inner.DeleteTeamAsync<TMember>(teamKey);
    }

    /// <summary>Restoring is authorized by the same rule as deleting — it undoes it.</summary>
    public async Task RestoreTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        await RequireDeleteAsync(teamKey);
        await _inner.RestoreTeamAsync<TMember>(teamKey);
    }

    /// <summary>
    /// Purging is the irreversible one and needs its own system scope, never a team-level grant.
    /// </summary>
    /// <remarks>
    /// <b>No <c>AllowTeamCreation</c> self-service path here</b>, unlike <see cref="RequireDeleteAsync"/>.
    /// A team administrator deleting their own team is recoverable and reasonable; destroying its storage
    /// outright is not something a tenant should reach by holding <c>team:manage</c>.
    /// </remarks>
    public async Task PurgeTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        if (!await _authorizer.HasSystemScopeAsync(SystemTeamScopes.Purge))
            throw new UnauthorizedAccessException(
                $"Permanently removing team '{teamKey}' requires the '{SystemTeamScopes.Purge}' system scope. " +
                $"'{SystemTeamScopes.Delete}' authorizes a recoverable delete, not this.");

        // Destroy the toolkit's own per-team data first. Purge drops the host's per-team database, which
        // does not reach the shared collections holding API keys, icons and support cases -- so without this
        // a purged tenant's credentials outlive it. Before the record, so a failure leaves a team that is
        // still visible and still purgeable rather than data nothing can find.
        if (_purgeCascade != null) await _purgeCascade.RunAsync(teamKey);

        await _inner.PurgeTeamAsync<TMember>(teamKey);
    }

    // Team administration (team:manage on the team, or the teams:manage system grant).
    public async Task RenameTeamAsync<TMember>(string teamKey, string name) where TMember : ITeamMember
    {
        await RequirePresentationManageAsync(teamKey, nameof(RenameTeamAsync));
        await _inner.RenameTeamAsync<TMember>(teamKey, name);
    }

    public async Task SetTeamConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null)
    {
        await RequireTeamScopeAsync(TeamScopes.Manage, teamKey);
        await _inner.SetTeamConsentAsync(teamKey, consentedRoles, accessLevel);
    }

    public async Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        await RequireTeamScopeAsync(_customRoleManageScope, teamKey);
        ValidateCustomRoles(customRoles);
        await _inner.SetTeamCustomRolesAsync(teamKey, customRoles);
    }

    // Member administration (member:manage on the team).
    public async Task AddMemberAsync(string teamKey, InviteUserModel model)
    {
        await RequireTeamScopeAsync(TeamScopes.MemberManage, teamKey);
        await _inner.AddMemberAsync(teamKey, model);
    }

    public async Task RemoveMemberAsync(string teamKey, string userKey)
    {
        await RequireTeamScopeAsync(TeamScopes.MemberManage, teamKey);
        await _inner.RemoveMemberAsync(teamKey, userKey);
    }

    public async Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel)
    {
        await RequireTeamScopeAsync(TeamScopes.MemberManage, teamKey);
        await _inner.SetMemberRoleAsync(teamKey, userKey, accessLevel);
    }

    public async Task SetMemberSuspendedAsync(string teamKey, string userKey, bool suspended)
    {
        await RequireTeamScopeAsync(TeamScopes.MemberManage, teamKey);
        await _inner.SetMemberSuspendedAsync(teamKey, userKey, suspended);
    }

    public async Task ExtendInvitationAsync(string teamKey, string inviteKey)
    {
        await RequireTeamScopeAsync(TeamScopes.MemberManage, teamKey);
        await _inner.ExtendInvitationAsync(teamKey, inviteKey);
    }

    public async Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles)
    {
        await RequireTeamScopeAsync(TeamScopes.MemberManage, teamKey);
        await _inner.SetMemberTenantRolesAsync(teamKey, userKey, tenantRoles);
    }

    public async Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides)
    {
        await RequireTeamScopeAsync(TeamScopes.MemberManage, teamKey);
        await _inner.SetMemberScopeOverridesAsync(teamKey, userKey, scopeOverrides);
    }

    public async Task SetMemberNameAsync(string teamKey, string userKey, string name)
    {
        await RequireTeamScopeAsync(TeamScopes.MemberManage, teamKey);
        await _inner.SetMemberNameAsync(teamKey, userKey, name);
    }

    // Cross-team member removal (users:manage system scope) — backs user deletion.
    public async Task<int> RemoveUserFromAllTeamsAsync(string userKey)
    {
        await RequireUsersManageAsync();
        return await _inner.RemoveUserFromAllTeamsAsync(userKey);
    }

    // Team icon (team:manage on the team, or the teams:manage system grant).
    public async Task SetTeamIconAsync(string teamKey, byte[] data, string contentType)
    {
        await RequirePresentationManageAsync(teamKey, nameof(SetTeamIconAsync));
        await _inner.SetTeamIconAsync(teamKey, data, contentType);
    }

    public async Task ClearTeamIconAsync(string teamKey)
    {
        await RequirePresentationManageAsync(teamKey, nameof(ClearTeamIconAsync));
        await _inner.ClearTeamIconAsync(teamKey);
    }

    private async Task RequireCreateAsync()
    {
        if (!_lifecycle.AllowTeamCreation)
            throw new UnauthorizedAccessException("Team creation is disabled (AllowTeamCreation = false).");
        if (!await _authorizer.IsAuthenticatedAsync())
            throw new UnauthorizedAccessException("Authentication is required to create a team.");
    }

    private async Task RequireAllTeamsReadAsync()
    {
        if (await _authorizer.HasSystemScopeAsync(SystemTeamScopes.Read)) return;
        throw new UnauthorizedAccessException(
            $"Listing all teams requires the '{SystemTeamScopes.Read}' system scope.");
    }

    private async Task RequireUsersManageAsync()
    {
        if (await _authorizer.HasSystemScopeAsync(SystemUserScopes.Manage)) return;
        throw new UnauthorizedAccessException(
            $"Removing a user from all teams requires the '{SystemUserScopes.Manage}' system scope.");
    }

    /// <summary>
    /// The two <b>presentational</b> team operations — rename and icon — accept either the in-team
    /// <see cref="TeamScopes.Manage"/> or the cross-team <see cref="SystemTeamScopes.Manage"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not used by <c>SetTeamConsentAsync</c> or <c>SetTeamCustomRolesAsync</c>, which keep
    /// requiring the in-team scope alone. Consent is a team's statement about what it exposes inbound and
    /// custom roles decide what a member may do — both authorization, neither presentation. An oversight
    /// role fixing a typo in a name is a different act from one granting itself reach into a tenant.
    /// </remarks>
    private async Task RequirePresentationManageAsync(string teamKey, string operation)
    {
        if (await _authorizer.HasSystemScopeAsync(SystemTeamScopes.Manage)) return;
        if (await _authorizer.HasTeamScopeAsync(TeamScopes.Manage, teamKey)) return;
        throw new UnauthorizedAccessException(
            $"'{operation}' on team '{teamKey}' requires '{TeamScopes.Manage}' on that team, " +
            $"or the '{SystemTeamScopes.Manage}' system scope.");
    }

    private async Task RequireDeleteAsync(string teamKey)
    {
        if (await _authorizer.HasSystemScopeAsync(SystemTeamScopes.Delete)) return;
        if (_lifecycle.AllowTeamCreation && await _authorizer.HasTeamScopeAsync(TeamScopes.Manage, teamKey)) return;
        throw new UnauthorizedAccessException(
            $"Deleting team '{teamKey}' requires '{TeamScopes.Manage}' on that team with AllowTeamCreation enabled, " +
            $"or the '{SystemTeamScopes.Delete}' system scope.");
    }

    private async Task RequireTeamScopeAsync(string scope, string teamKey)
    {
        if (!await _authorizer.HasTeamScopeAsync(scope, teamKey))
            throw new UnauthorizedAccessException($"This operation on team '{teamKey}' requires the '{scope}' scope on that team.");
    }

    /// <summary>
    /// Guards against privilege escalation and ambiguity when defining custom roles: every scope must be
    /// app-registered (<see cref="IScopeRegistry"/>) and must not be grant-only, names must be non-empty
    /// and unique, and must not collide with a code-registered role name.
    /// </summary>
    /// <remarks>
    /// The grant-only check is what keeps such a scope out of reach of the very administrators it is meant
    /// to exclude: defining custom roles is authorized by <c>DynamicTenantRoleOptions.ManageScope</c>
    /// (<c>team:manage</c> by default), which every team administrator holds, so without it an
    /// administrator could name the scope in a role of their own and assign it to themselves.
    /// </remarks>
    private void ValidateCustomRoles(IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        if (customRoles == null) return;

        var registeredScopes = _scopeRegistry?.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var grantOnlyScopes = _scopeRegistry?.All.Where(s => s.GrantOnly).Select(s => s.Name).ToHashSet(StringComparer.Ordinal)
                              ?? [];
        var codeRoleNames = _tenantRoleRegistry?.All.Select(r => r.Name).ToHashSet(StringComparer.Ordinal)
                            ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in customRoles)
        {
            if (string.IsNullOrWhiteSpace(role.Name))
                throw new InvalidOperationException("A custom role name must not be empty.");

            var name = role.Name.Trim();

            if (!seen.Add(name))
                throw new InvalidOperationException($"Duplicate custom role name '{name}'.");

            if (codeRoleNames.Contains(name))
                throw new InvalidOperationException($"Custom role '{name}' collides with a code-registered role of the same name.");

            foreach (var scope in role.Scopes ?? [])
            {
                if (registeredScopes == null || !registeredScopes.Contains(scope))
                    throw new InvalidOperationException(
                        $"Custom role '{name}' references scope '{scope}', which is not an app-registered scope.");

                if (grantOnlyScopes.Contains(scope))
                    throw new InvalidOperationException(
                        $"Custom role '{name}' references scope '{scope}', which is grant-only and cannot be granted by a tenant-defined role. It is held only through a code-registered role.");
            }
        }
    }
}
