using Microsoft.Extensions.Logging;
using Tharga.Toolkit;

namespace Tharga.Team;

public abstract class TeamServiceBase : ITeamService
{
    private readonly IUserService _userService;
    private readonly ILogger<TeamServiceBase> _logger;
    private readonly IIconStore _iconStore;
    private readonly ITeamCache _cache;
    private readonly TeamDeleteMode _configuredDeleteMode;
    private readonly InvitationOptions _invitationOptions;

    /// <param name="userService">Resolves the calling user.</param>
    /// <param name="logger">Optional. Used to report ambiguous member matches.</param>
    /// <param name="iconStore">Optional. Required only for team icons; see <see cref="SetTeamIconAsync"/>.</param>
    /// <param name="cache">
    /// Where the membership and custom-role lookups are kept. Defaults to the process-local
    /// <see cref="InMemoryTeamCache"/>, which is correct for a single instance only —
    /// <b>forward this parameter from your own service's constructor</b> so a shared implementation can be
    /// registered, or a multi-instance deployment will not see permission changes made through another
    /// instance. See <see cref="ITeamCache"/>.
    /// </param>
    protected TeamServiceBase(
        IUserService userService,
        ILogger<TeamServiceBase> logger = null,
        IIconStore iconStore = null,
        ITeamCache cache = null,
        TeamDeleteMode deleteMode = TeamDeleteMode.Soft,
        InvitationOptions invitationOptions = null)
    {
        _userService = userService;
        _logger = logger;
        _iconStore = iconStore;
        _cache = cache ?? InMemoryTeamCache.Shared;
        _configuredDeleteMode = deleteMode;
        _invitationOptions = invitationOptions ?? new InvitationOptions();
    }

    /// <summary>
    /// What deleting a team does in this store. Defaults to whatever the host configured, or
    /// <see cref="TeamDeleteMode.Soft"/>.
    /// </summary>
    /// <remarks>
    /// <b>An optional constructor parameter rather than a required one</b>, and a virtual property rather
    /// than a field read, so neither a derived host that never passes it nor one that wants to decide the
    /// mode itself has to change. Both matter in a patch.
    /// <para>
    /// Note this is only half the answer — see <see cref="SupportsSoftDelete"/>. A store that cannot mark a
    /// team deleted deletes outright whatever this says.
    /// </para>
    /// </remarks>
    protected virtual TeamDeleteMode DeleteMode => _configuredDeleteMode;

    /// <summary>
    /// The cache this instance actually ended up with, so <see cref="TeamCacheWiring"/> can tell a forwarded
    /// <see cref="ITeamCache"/> from the process-local fallback. Internal: a diagnostic, not API.
    /// </summary>
    internal ITeamCache CacheInUse => _cache;

    public event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent;
    public event EventHandler<SelectTeamEventArgs> SelectTeamEvent;

    protected abstract IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user);
    protected abstract Task<ITeam> GetTeamAsync(string teamKey);
    protected abstract Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName = null);
    protected abstract Task SetTeamNameAsync(string teamKey, string name);
    protected abstract Task DeleteTeamAsync(string teamKey);

    /// <summary>
    /// Whether this store can soft-delete. <c>false</c> by default, so a store that has not implemented it
    /// keeps deleting exactly as before.
    /// </summary>
    /// <remarks>
    /// <b>This is what keeps soft delete a patch rather than a break.</b> The default delete mode is
    /// <see cref="TeamDeleteMode.Soft"/>, but a host deriving from this class predates the feature and
    /// cannot mark a team deleted. Reporting <c>false</c> makes the mode resolve to
    /// <see cref="TeamDeleteMode.Hard"/> for that store — the behaviour it already had — instead of failing
    /// on an operation it cannot perform.
    /// <para>
    /// Surfaces are expected to consult this before offering restore or purge; a control that throws when
    /// clicked is worse than no control.
    /// </para>
    /// </remarks>
    protected virtual bool SupportsSoftDelete => false;

    /// <summary>
    /// Marks the team deleted without removing it, recording when and by whom.
    /// </summary>
    /// <remarks>
    /// <b>Virtual, never abstract</b> — an abstract member here breaks every derived host at compile time.
    /// The default delegates to <see cref="DeleteTeamAsync(string)"/>, so a store that has not implemented
    /// soft delete performs the irreversible delete it always did rather than throwing. Paired with
    /// <see cref="SupportsSoftDelete"/>, which tells callers which of the two they will get.
    /// </remarks>
    protected virtual Task SoftDeleteTeamAsync(string teamKey, string deletedBy) => DeleteTeamAsync(teamKey);

    /// <summary>Clears the deleted mark, returning the team to normal use.</summary>
    /// <remarks>
    /// Unreachable unless <see cref="SupportsSoftDelete"/> is <c>true</c> — there is nothing to restore in a
    /// store that deletes outright — so the default throws rather than pretending to succeed.
    /// </remarks>
    protected virtual Task RestoreTeamAsync(string teamKey)
        => throw new NotSupportedException(
            $"This team store does not support soft delete, so '{teamKey}' cannot be restored. " +
            $"Override {nameof(SupportsSoftDelete)} and {nameof(SoftDeleteTeamAsync)} to enable it.");

    /// <summary>
    /// Removes the team permanently, including its storage.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="DeleteTeamAsync(string)"/> — precisely what deleting a team meant before
    /// soft delete existed — so every store gains a working purge without implementing anything.
    /// <para>
    /// This is the only operation that needs whatever privilege the adapter requires to drop a team's data.
    /// For the MongoDB adapter in a per-team-database deployment that is <c>dropDatabase</c>, which is why
    /// confining it here is the point of Tharga/Team#224.
    /// </para>
    /// </remarks>
    protected virtual Task PurgeTeamAsync(string teamKey) => DeleteTeamAsync(teamKey);

    /// <summary>
    /// Whether a team key is already taken — <b>including by a soft-deleted team</b>.
    /// </summary>
    /// <remarks>
    /// <b>A soft-deleted team still owns its key, and this is what enforces that.</b> Key generation used
    /// the ordinary team read, which now excludes deleted teams — so without this a deleted team's key
    /// reads as free and gets reissued. In a deployment that derives a team's database name from its key,
    /// the new team would then be pointed at the deleted team's data: the corruption Tharga/Team#224 is
    /// about, arriving by a different route.
    /// <para>
    /// The default keeps the old behaviour — live teams only — so a store that cannot see deleted teams is
    /// unaffected, which is also the store that cannot soft-delete and therefore never has one.
    /// </para>
    /// </remarks>
    protected virtual async Task<bool> IsTeamKeyInUseAsync(string teamKey)
        => await GetTeamAsync(teamKey) != null;
    protected abstract Task AddTeamMemberAsync(string teamKey, InviteUserModel model);
    protected abstract Task RemoveTeamMemberAsync(string teamKey, string userKey);
    protected abstract Task<ITeam> SetTeamMemberInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept);
    protected abstract Task SetTeamMemberLastSeenAsync(string teamKey, string userKey);
    protected abstract Task<ITeamMember> GetTeamMembersAsync(string teamKey, string userKey);
    protected abstract Task SetTeamMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel);
    protected abstract Task SetTeamMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles);
    protected abstract Task SetTeamMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides);
    protected abstract Task SetTeamMemberNameAsync(string teamKey, string userKey, string name);

    /// <summary>Persists a member's suspended state. Override to support suspending members.</summary>
    /// <remarks>
    /// Virtual with a throwing body rather than abstract: adding an abstract member here would break
    /// every host that already derives from this class. Throwing rather than no-opping for the same
    /// reason the user and key equivalents do — a suspension silently skipped is a containment reported
    /// but never applied.
    /// </remarks>
    protected virtual Task SetTeamMemberSuspendedAsync(string teamKey, string userKey, DateTime? suspendedAt, string suspendedBy)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetTeamMemberSuspendedAsync)}. Implement it, " +
            $"and declare {nameof(ITeamMember.SuspendedAt)}/{nameof(ITeamMember.SuspendedBy)} on your " +
            $"member entity, to support suspending members.");
    protected abstract Task SetTeamConsentInternalAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel);
    protected abstract IAsyncEnumerable<ITeam> GetConsentedTeamsInternalAsync(string[] userRoles);
    protected abstract Task SetTeamCustomRolesInternalAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles);

    public async IAsyncEnumerable<ITeam> GetTeamsAsync()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) yield break;

        await foreach (var team in GetTeamsAsync(user))
        {
            yield return team;
        }
    }

    public async IAsyncEnumerable<ITeam<TMember>> GetTeamsAsync<TMember>() where TMember : ITeamMember
    {
        var user = await GetCurrentUserAsync();
        if (user == null) yield break;

        await foreach (var team in GetTeamsAsync(user))
        {
            yield return (ITeam<TMember>)team;
        }
    }

    /// <summary>
    /// Backs <see cref="GetAllTeamsAsync()"/>. Virtual rather than abstract so existing derived services
    /// keep compiling; the default returns nothing, and storage-backed bases override it.
    /// </summary>
    protected virtual async IAsyncEnumerable<ITeam> GetAllTeamsInternalAsync()
    {
        await Task.CompletedTask;
        yield break;
    }

    public virtual IAsyncEnumerable<ITeam> GetAllTeamsAsync() => GetAllTeamsInternalAsync();

    public virtual async IAsyncEnumerable<ITeam<TMember>> GetAllTeamsAsync<TMember>() where TMember : ITeamMember
    {
        await foreach (var team in GetAllTeamsInternalAsync())
        {
            yield return (ITeam<TMember>)team;
        }
    }

    public async Task<ITeam<TMember>> GetTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        var team = await GetTeamAsync(teamKey);
        return (ITeam<TMember>)team;
    }

    public Task<ITeam> GetTeamByKeyAsync(string teamKey) => GetTeamAsync(teamKey);

    public async Task<ITeam> CreateTeamAsync(string name)
    {
        var user = await RequireCurrentUserAsync();

        var displayName = ResolveDisplayName(user);
        name ??= $"{displayName}'s team";

        var teamKey = await GetRandomUnsusedTeamKey();

        var team = await CreateTeamAsync(teamKey, name, user, displayName);

        // A deleted team's key becomes available again -- GetRandomUnsusedTeamKey only checks that no team
        // holds it -- so both ends of that lifecycle clear the entry rather than trusting the other to have.
        await _cache.RemoveCustomRolesAsync(teamKey);

        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
        SelectTeamEvent?.Invoke(this, new SelectTeamEventArgs(team));

        return team;
    }

    // Authorization (team:manage / teams:delete) is enforced by AuthorizationTeamServiceDecorator at the
    // service boundary, so it applies uniformly to admin users and team API keys. These methods perform the
    // operation; they assume the caller is already authorized.
    public async Task RenameTeamAsync<TMember>(string teamKey, string name) where TMember : ITeamMember
    {
        await SetTeamNameAsync(teamKey, name);

        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    /// <summary>
    /// Deletes the team — soft by default, so it can be restored, and so deleting needs no privilege to
    /// drop storage.
    /// </summary>
    /// <remarks>
    /// Resolves to a hard delete when the host asks for one, and also when the store cannot soft-delete:
    /// see <see cref="SupportsSoftDelete"/>. Either way the team disappears from every read, which is why
    /// the change of default is invisible through this API.
    /// </remarks>
    public async Task DeleteTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        // Captured before the delete, and that ordering is required: once a team is soft-deleted the
        // filtered read returns null, so afterwards there is no roster left to evict from the cache.
        var memberKeys = await MemberKeysForCacheEvictionAsync<TMember>(teamKey);

        if (UseSoftDelete)
        {
            var deletedBy = (await GetCurrentUserAsync())?.Key;
            await SoftDeleteTeamAsync(teamKey, deletedBy);
        }
        else
        {
            await DeleteTeamAsync(teamKey);
        }

        await AfterTeamRemovedFromUseAsync(teamKey, memberKeys);
    }

    /// <summary>Restores a soft-deleted team.</summary>
    public async Task RestoreTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        await RestoreTeamAsync(teamKey);

        // Evicted *after* the restore, the mirror of delete. While the team was deleted, any membership
        // lookup cached a null — GetTeamMemberAsync caches the miss as well as the hit — and a cached null
        // would keep denying access to a team that is live again.
        var memberKeys = await MemberKeysForCacheEvictionAsync<TMember>(teamKey);

        await AfterTeamRemovedFromUseAsync(teamKey, memberKeys);
    }

    /// <summary>
    /// Removes a team permanently, including its storage. Irreversible.
    /// </summary>
    /// <remarks>
    /// The only operation needing the privilege to drop a team's data. A store refusal surfaces as
    /// <see cref="TeamStorageException"/> rather than as the store's own exception.
    /// </remarks>
    public async Task PurgeTeamAsync<TMember>(string teamKey) where TMember : ITeamMember
    {
        var memberKeys = await MemberKeysForCacheEvictionAsync<TMember>(teamKey);

        await PurgeTeamAsync(teamKey);

        await AfterTeamRemovedFromUseAsync(teamKey, memberKeys);
    }

    /// <summary>
    /// Shared tail of every operation that takes a team out of use, so soft delete, hard delete and purge
    /// cannot drift on cache invalidation or on notifying the UI.
    /// </summary>
    private async Task AfterTeamRemovedFromUseAsync(string teamKey, IReadOnlyCollection<string> memberKeys)
    {
        await _cache.RemoveCustomRolesAsync(teamKey);

        // <b>Without this a soft-deleted team keeps authorizing.</b> GetTeamMemberAsync reads the member
        // cache before the store, so the repository's deleted-team filter is never consulted for a caller
        // whose membership is already cached — they stay authorized on a deleted team until the entry
        // expires. Silent, and an authorization failure rather than a display one.
        //
        // Every other member-changing operation on this class already evicts the same way; deletion was
        // the omission. Done with the existing per-member operation rather than a new ITeamCache member,
        // so no custom cache implementation has to change and none can silently skip it.
        foreach (var memberKey in memberKeys)
        {
            await _cache.RemoveMemberAsync(teamKey, memberKey);
        }

        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    /// <summary>
    /// The member keys whose cache entries have to be evicted when a team enters or leaves use.
    /// </summary>
    /// <remarks>
    /// Best-effort by design: a store that cannot answer must not block a delete. An empty result degrades
    /// to today's behaviour — entries expire on their own — rather than failing the operation.
    /// </remarks>
    private async Task<IReadOnlyCollection<string>> MemberKeysForCacheEvictionAsync<TMember>(string teamKey)
        where TMember : ITeamMember
    {
        try
        {
            var team = await GetTeamAsync<TMember>(teamKey);
            return [.. (team?.Members ?? []).Select(x => x.Key).Where(x => !string.IsNullOrEmpty(x))];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Whether this delete should be soft: the host asked for it <b>and</b> the store can do it.
    /// </summary>
    private bool UseSoftDelete => DeleteMode == TeamDeleteMode.Soft && SupportsSoftDelete;

    /// <inheritdoc cref="ITeamManagementService.GetTeamMemberAsync"/>
    public async Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey)
    {
        // What comes back for an invited member depends on the host's GetTeamMembersAsync: the MongoDB
        // store resolves through a State == Member query and so returns null, while a store written
        // differently may return the invitee. Neither is wrong, but nothing may depend on which -- code
        // that must tell the states apart reads the roster through GetMembersAsync instead.
        var cached = await _cache.GetMemberAsync(teamKey, userKey);
        if (cached.Found) return cached.Value;

        var teamMember = await GetTeamMembersAsync(teamKey, userKey);

        await _cache.SetMemberAsync(teamKey, userKey, teamMember);

        return teamMember;
    }

    /// <summary>
    /// Every member on the team's roster, <b>in any <see cref="MembershipState"/></b> — including
    /// invited and rejected.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="GetTeamMemberAsync"/>, which returns only an active membership and
    /// cannot tell a pending invitee from a stranger. Reach for this one whenever the difference matters.
    /// </remarks>
    public virtual async IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey)
    {
        var team = await GetTeamAsync(teamKey);
        var members = GetMembersFromTeam(team);
        if (members == null) yield break;
        foreach (var member in members)
        {
            yield return member;
        }
    }

    /// <summary>
    /// Invites someone, or <b>renews the invitation they already have</b>.
    /// </summary>
    /// <remarks>
    /// <b>Re-inviting an address that already has an outstanding invitation keeps its code.</b> It used to
    /// add a second member row carrying a second live code for one person, which is two working links to the
    /// same seat and one of them impossible to withdraw. Renewing instead means someone who has already
    /// mailed a link can give it more time without the recipient's link dying — the requirement behind
    /// Tharga/Team#249.
    /// <para>
    /// <b>Changed details are still applied.</b> An invitation renewed with a different access level or name
    /// takes them, because an administrator who re-invites at a different level plainly means the new one;
    /// silently keeping the old level would be the more surprising reading of the same click.
    /// </para>
    /// <para>
    /// The expiry is only moved when a <see cref="InvitationOptions.Lifetime"/> is configured. Without one
    /// invitations do not expire, so there is nothing to extend and a store that has not implemented the
    /// expiry seam is not asked to.
    /// </para>
    /// </remarks>
    public async Task AddMemberAsync(string teamKey, InviteUserModel model)
    {
        var outstanding = string.IsNullOrWhiteSpace(model?.Email)
            ? null
            : await GetMembersAsync(teamKey).FirstOrDefaultAsync(x =>
                x.Invitation != null &&
                string.Equals(x.Invitation.EMail, model.Email, StringComparison.OrdinalIgnoreCase));

        if (outstanding != null)
        {
            if (outstanding.AccessLevel != model.AccessLevel)
                await SetTeamMemberRoleAsync(teamKey, outstanding.Key, model.AccessLevel);

            if (!string.IsNullOrWhiteSpace(model.Name) && !string.Equals(model.Name, outstanding.Name, StringComparison.Ordinal))
                await SetTeamMemberNameAsync(teamKey, outstanding.Key, model.Name);

            if (_invitationOptions.Lifetime != null)
                await ExtendInvitationAsync(teamKey, outstanding.Invitation.InviteKey);

            return;
        }

        await AddTeamMemberAsync(teamKey, model);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    public async Task RemoveMemberAsync(string teamKey, string userKey)
    {
        var team = await GetTeamAsync(teamKey);
        var members = GetMembersFromTeam(team);
        if (members != null)
        {
            var member = members.PickOneOrDefault(x => x.Key == userKey, _logger, teamKey, userKey);
            if (member != null)
            {
                if (member.AccessLevel == AccessLevel.Owner)
                    throw new InvalidOperationException("The owner cannot leave the team. Transfer ownership first.");

                var user = await RequireCurrentUserAsync();
                if (member.Key == user.Key && member.AccessLevel == AccessLevel.Administrator)
                {
                    var otherAdminsOrOwners = members.Count(x =>
                        x.Key != userKey &&
                        x.State == MembershipState.Member &&
                        x.AccessLevel <= AccessLevel.Administrator);
                    if (otherAdminsOrOwners == 0)
                        throw new InvalidOperationException("Cannot leave the team as the last administrator.");
                }
            }
        }

        await RemoveTeamMemberAsync(teamKey, userKey);
        await _cache.RemoveMemberAsync(teamKey, userKey);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    /// <summary>
    /// Sets a member's access level. Ownership is not settable here — it changes only through
    /// <see cref="TransferOwnershipAsync{TMember}"/>, which checks that the caller is the current owner.
    /// </summary>
    /// <remarks>
    /// Both directions are refused. Granting Owner would let any holder of the member-manage scope promote
    /// themselves past that check; demoting the sitting owner would leave a team nobody can transfer, because
    /// transfer requires the caller to be the owner. Transfer itself is unaffected — it calls the protected
    /// <see cref="SetTeamMemberRoleAsync"/> directly.
    /// </remarks>
    public async Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel)
    {
        if (accessLevel == AccessLevel.Owner)
            throw new InvalidOperationException("A member cannot be made owner directly. Transfer ownership instead.");

        var current = await GetTeamMemberAsync(teamKey, userKey);
        if (current?.AccessLevel == AccessLevel.Owner)
            throw new InvalidOperationException("The owner's access level cannot be changed. Transfer ownership first.");

        await SetTeamMemberRoleAsync(teamKey, userKey, accessLevel);
        await _cache.RemoveMemberAsync(teamKey, userKey);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    /// <remarks>
    /// Two refusals, both mirroring guards this class already applies elsewhere. <b>The Owner cannot be
    /// suspended</b> — the same reason the owner cannot leave and cannot be demoted: it would leave a team
    /// whose ownership nobody can transfer, since transfer requires the caller to be the owner. <b>A
    /// member cannot suspend themselves</b>, so an administrator who does it needs a second one to undo
    /// it, and somebody is always left holding <c>member:manage</c>.
    /// <para>
    /// The member cache is dropped on both directions, or the claims builder keeps reading the old state
    /// and the suspension takes effect only after the entry ages out.
    /// </para>
    /// </remarks>
    public async Task SetMemberSuspendedAsync(string teamKey, string userKey, bool suspended)
    {
        // The whole roster, not GetTeamMemberAsync. That path resolves through the store's
        // "teams I am a member of" query, which filters on State == Member -- so an invited person comes
        // back null and would be reported as not being in the team at all, which is both wrong and
        // unhelpful. Reading the team directly is the only way to tell the two apart.
        var member = await GetMembersAsync(teamKey).FirstOrDefaultAsync(x => x.Key == userKey);
        if (member == null)
            throw new InvalidOperationException($"User '{userKey}' is not a member of team '{teamKey}'.");

        if (member.State != null && member.State != MembershipState.Member)
        {
            throw new InvalidOperationException(
                $"'{userKey}' has not accepted the invitation to team '{teamKey}', so there is no access " +
                $"to suspend. Withdraw the invitation instead.");
        }

        if (suspended)
        {
            if (member.AccessLevel == AccessLevel.Owner)
                throw new InvalidOperationException("The owner cannot be suspended. Transfer ownership first.");

            var caller = await GetCurrentUserAsync();
            if (caller != null && string.Equals(caller.Key, userKey, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("You cannot suspend your own membership. Ask another administrator to do it.");
        }

        var actor = suspended ? (await GetCurrentUserAsync())?.Key : null;
        await SetTeamMemberSuspendedAsync(teamKey, userKey, suspended ? DateTime.UtcNow : null, actor);

        await _cache.RemoveMemberAsync(teamKey, userKey);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    /// <inheritdoc />
    public Task<string> GetTeamKeyByInviteKeyAsync(string inviteKey)
    {
        return string.IsNullOrWhiteSpace(inviteKey)
            ? Task.FromResult<string>(null)
            : GetTeamKeyByInviteKeyInternalAsync(inviteKey);
    }

    /// <inheritdoc />
    public async Task ExtendInvitationAsync(string teamKey, string inviteKey)
    {
        var invitation = await GetInvitationInternalAsync(teamKey, inviteKey);
        if (invitation == null)
            throw new InvalidOperationException($"No outstanding invitation matches that code on team '{teamKey}'.");

        // Null lifetime means invitations do not expire, so extending clears whatever expiry the record was
        // carrying rather than inventing one. The code is untouched either way -- that is the whole point.
        var expiresAt = _invitationOptions.Lifetime == null
            ? (DateTime?)null
            : DateTime.UtcNow + _invitationOptions.Lifetime.Value;

        await SetTeamMemberInvitationExpiryAsync(teamKey, inviteKey, expiresAt);
    }

    public async Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles)
    {
        await SetTeamMemberTenantRolesAsync(teamKey, userKey, tenantRoles);
        await _cache.RemoveMemberAsync(teamKey, userKey);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    public async Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides)
    {
        await SetTeamMemberScopeOverridesAsync(teamKey, userKey, scopeOverrides);
        await _cache.RemoveMemberAsync(teamKey, userKey);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    public async Task SetMemberNameAsync(string teamKey, string userKey, string name)
    {
        await SetTeamMemberNameAsync(teamKey, userKey, name);
        await _cache.RemoveMemberAsync(teamKey, userKey);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    public async Task SetInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept)
    {
        if (accept)
        {
            // Enforced here rather than at the surface that reads the invitation, because this is where both
            // paths meet: ITeamManagementService delegates to ITeamService, and a host calling ITeamService
            // directly reaches the same method. A check on the screen would decorate one of the two.
            //
            // Declining is deliberately still allowed once expired -- refusing it would leave the row behind
            // with no way for the invitee to clear it, and declining grants nothing.
            var invitation = await GetInvitationInternalAsync(teamKey, inviteKey);
            if (InvitationPolicy.HasExpired(invitation, _invitationOptions.Lifetime, DateTime.UtcNow))
                throw new InvalidOperationException(
                    $"The invitation to team '{teamKey}' expired on {InvitationPolicy.ExpiresAt(invitation, _invitationOptions.Lifetime):u}.");

            // Capture the admin-entered Member.Name *before* the accept clears it, so we can
            // promote it to User.Name (only-if-empty) once the response has been recorded.
            var seedName = await GetInvitedMemberNameAsync(teamKey, inviteKey);

            var team = await SetTeamMemberInvitationResponseAsync(teamKey, userKey, inviteKey, true);

            if (!string.IsNullOrWhiteSpace(seedName))
            {
                await _userService.SeedUserNameAsync(userKey, seedName);
            }

            TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
            SelectTeamEvent?.Invoke(this, new SelectTeamEventArgs(team));
        }
        else
        {
            await SetTeamMemberInvitationResponseAsync(teamKey, userKey, inviteKey, false);
            TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
        }

        await _cache.RemoveMemberAsync(teamKey, userKey);
    }

    /// <summary>
    /// Look up the (admin-entered) Name of the member identified by <paramref name="inviteKey"/>
    /// inside the given team. Used to capture the invitation Name *before* accept clears it,
    /// so it can be promoted to <c>User.Name</c>. Default implementation returns null;
    /// derivatives that have access to the typed team document override it.
    /// </summary>
    protected virtual Task<string> GetInvitedMemberNameAsync(string teamKey, string inviteKey)
    {
        return Task.FromResult<string>(null);
    }

    /// <summary>
    /// The outstanding invitation matching <paramref name="inviteKey"/> on <paramref name="teamKey"/>, or
    /// null. Backs expiry enforcement.
    /// </summary>
    /// <remarks>
    /// <b>Virtual rather than abstract</b>, like the other members added after the seam was first drawn, so a
    /// host with its own store keeps compiling. The default returns null, which reads as "no expiry to
    /// enforce".
    /// <para>
    /// <b>That default is a hole if a lifetime is configured and this is not overridden</b> — expiry would
    /// silently not apply. It is a startup check rather than a silent default for exactly that reason: see
    /// <c>InvitationExpiryWiringCheck</c>, which fails the boot naming this method when
    /// <see cref="InvitationOptions.Lifetime"/> is set and the store cannot answer it. Nothing is asked of a
    /// host that has not opted into expiry.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Backs <see cref="GetTeamKeyByInviteKeyAsync"/>. Null when nothing matches, when more than one team
    /// matches, or when this store cannot look an invitation up without its team.
    /// </summary>
    /// <remarks>
    /// <b>Virtual, and null is a legitimate answer</b> — unlike the expiry seam, which throws. A store that
    /// cannot answer this loses nothing it had: links minted before this existed carry their team key and
    /// still resolve. Only the short link form needs it, so degrading is the correct behaviour rather than a
    /// hidden failure.
    /// </remarks>
    protected virtual Task<string> GetTeamKeyByInviteKeyInternalAsync(string inviteKey)
    {
        return Task.FromResult<string>(null);
    }

    protected virtual Task<Invitation> GetInvitationInternalAsync(string teamKey, string inviteKey)
    {
        return Task.FromResult<Invitation>(null);
    }

    /// <summary>
    /// Moves the expiry of the invitation matching <paramref name="inviteKey"/>, leaving its code alone.
    /// </summary>
    /// <remarks>
    /// <b>Throws rather than no-opping when unimplemented</b>, unlike
    /// <see cref="GetInvitationInternalAsync"/>. The difference is what silence would mean: a store that
    /// cannot read an expiry has nothing to enforce, but a store that silently discards an extension reports
    /// success for an invitation that stays expired, and the operator finds out from the person who could
    /// not accept it.
    /// </remarks>
    protected virtual Task SetTeamMemberInvitationExpiryAsync(string teamKey, string inviteKey, DateTime? expiresAt)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetTeamMemberInvitationExpiryAsync)}. Implement " +
            $"it to support extending an invitation.");

    public async Task SetMemberLastSeenAsync(string teamKey)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return;
        await SetTeamMemberLastSeenAsync(teamKey, user.Key);
        await _cache.RemoveMemberAsync(teamKey, user.Key);
    }

    /// <summary>
    /// Backs <see cref="RemoveUserFromAllTeamsAsync"/>. Virtual rather than abstract so existing derived
    /// services keep compiling; the default throws rather than returning 0, since a silent no-op on a
    /// deletion path would hide the missing implementation. Storage-backed bases override it.
    /// </summary>
    /// <summary>
    /// Backs <see cref="GetTeamsForUserWithAccessLevelAsync"/>. Virtual-throw rather than returning an
    /// empty list, because an empty list is indistinguishable from "this user owns nothing" — and the
    /// caller uses that answer to decide whether deleting them is safe. A silent empty default would
    /// suppress exactly the warning this exists to raise.
    /// </summary>
    protected virtual Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelInternalAsync(string userKey, AccessLevel accessLevel)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(GetTeamsForUserWithAccessLevelInternalAsync)}. " +
            "Implement it so user deletion can warn about teams the user owns.");

    public Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelAsync(string userKey, AccessLevel accessLevel)
        => GetTeamsForUserWithAccessLevelInternalAsync(userKey, accessLevel);

    protected virtual Task<int> RemoveUserFromAllTeamsInternalAsync(string userKey)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(RemoveUserFromAllTeamsInternalAsync)}. " +
            $"Implement it to support user deletion (the '{SystemUserScopes.Manage}' system scope).");

    /// <summary>
    /// Backs <see cref="SetTeamIconAsync"/> / <see cref="ClearTeamIconAsync"/> — persists the icon
    /// reference (or null to clear) on the team document. Virtual-throw so existing derived services keep
    /// compiling; storage-backed bases override it.
    /// </summary>
    protected virtual Task SetTeamIconReferenceInternalAsync(string teamKey, string reference)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetTeamIconReferenceInternalAsync)}. Implement it to support team icons.");

    public async Task SetTeamIconAsync(string teamKey, byte[] data, string contentType)
    {
        var store = RequireIconStore();

        var team = await GetTeamAsync(teamKey);
        var previousReference = team?.Icon;

        var reference = await store.SaveAsync(IconKind.Team, teamKey, data, contentType);
        await SetTeamIconReferenceInternalAsync(teamKey, reference);

        if (!string.IsNullOrEmpty(previousReference))
            await store.DeleteAsync(previousReference);

        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    public async Task ClearTeamIconAsync(string teamKey)
    {
        var store = RequireIconStore();

        var team = await GetTeamAsync(teamKey);
        var previousReference = team?.Icon;
        if (string.IsNullOrEmpty(previousReference)) return;

        await SetTeamIconReferenceInternalAsync(teamKey, null);
        await store.DeleteAsync(previousReference);

        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    private IIconStore RequireIconStore()
        => _iconStore ?? throw new NotSupportedException(
            "No IIconStore was supplied to this service. Team icons require one, and there are two ways to " +
            "be missing it: (a) none is registered — the built-in MongoIconStore comes from " +
            "AddThargaTeamRepository, or supply your own via o.AddIconStore<T>(); or (b) it IS registered " +
            "but this service did not receive it — TeamServiceRepositoryBase takes an optional " +
            "'IIconStore iconStore = null' constructor parameter, so a subclass that does not forward it " +
            "gets null here. See docs/articles/icons.md.");

    public async Task<int> RemoveUserFromAllTeamsAsync(string userKey)
    {
        var count = await RemoveUserFromAllTeamsInternalAsync(userKey);

        await _cache.RemoveMembersForUserAsync(userKey);

        if (count > 0)
        {
            TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
        }

        return count;
    }

    public async Task<SetOwnerResult> SetOwnerAsync<TMember>(string teamKey, string newOwnerUserKey) where TMember : ITeamMember
    {
        var team = await GetTeamAsync<TMember>(teamKey)
            ?? throw new InvalidOperationException($"Team '{teamKey}' was not found.");

        var members = team.Members?.Cast<ITeamMember>().ToArray() ?? [];

        if (!TeamOwnership.CanSetOwner(members, newOwnerUserKey))
            throw new InvalidOperationException(
                $"User '{newOwnerUserKey}' is not a member of team '{teamKey}'. An owner is chosen from " +
                "the team's existing members, so this cannot introduce someone new to it.");

        // Already correct. The caller is typically a sync reconciling teams from another system, where this
        // is the common case rather than a mistake -- so it returns quietly instead of making every such
        // caller catch an exception, and writes no audit entry for a change that did not happen.
        if (TeamOwnership.IsSoleOwner(members, newOwnerUserKey)) return SetOwnerResult.NoChange;

        var demoted = TeamOwnership.OwnersToDemote(members, newOwnerUserKey);

        // Promote before demoting. The reverse order leaves the team momentarily ownerless, and a failure
        // between the two writes would leave it that way permanently -- the state this operation exists to
        // repair.
        await SetTeamMemberRoleAsync(teamKey, newOwnerUserKey, AccessLevel.Owner);
        await _cache.RemoveMemberAsync(teamKey, newOwnerUserKey);

        foreach (var owner in demoted)
        {
            await SetTeamMemberRoleAsync(teamKey, owner.Key, AccessLevel.Administrator);
            await _cache.RemoveMemberAsync(teamKey, owner.Key);
        }

        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());

        return new SetOwnerResult(true, demoted.Select(x => x.Key).ToArray());
    }

    public async Task TransferOwnershipAsync<TMember>(string teamKey, string newOwnerUserKey) where TMember : ITeamMember
    {
        var user = await RequireCurrentUserAsync();
        var team = await GetTeamAsync<TMember>(teamKey);
        var currentOwner = team.Members.PickOneOrDefault(x => x.Key == user.Key, _logger, teamKey, user.Key);
        if (currentOwner == null || currentOwner.AccessLevel != AccessLevel.Owner)
            throw new InvalidOperationException("Only the current owner can transfer ownership.");

        var newOwner = team.Members.PickOneOrDefault(x => x.Key == newOwnerUserKey, _logger, teamKey, newOwnerUserKey);
        if (newOwner == null)
            throw new InvalidOperationException($"User '{newOwnerUserKey}' is not a member of this team.");
        if (newOwner.Key == user.Key)
            throw new InvalidOperationException("Cannot transfer ownership to yourself.");

        await SetTeamMemberRoleAsync(teamKey, newOwnerUserKey, AccessLevel.Owner);
        await SetTeamMemberRoleAsync(teamKey, user.Key, AccessLevel.Administrator);
        await _cache.RemoveMemberAsync(teamKey, newOwnerUserKey);
        await _cache.RemoveMemberAsync(teamKey, user.Key);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    public async Task SetTeamConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null)
    {
        await SetTeamConsentInternalAsync(teamKey, consentedRoles, accessLevel);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    public IAsyncEnumerable<ITeam> GetConsentedTeamsAsync(string[] userRoles)
    {
        return GetConsentedTeamsInternalAsync(userRoles);
    }

    /// <inheritdoc cref="ITeamManagementService.GetTeamCustomRolesAsync"/>
    /// <remarks>
    /// Served from <see cref="ITeamCache"/>, because the claims path reads this on every authenticating
    /// request once <c>AddThargaDynamicTenantRoles</c> is registered and it reads the whole team document to
    /// answer. <b>The custom roles are cached; the team is not</b> — the team carries the member roster, and
    /// <see cref="SetMemberSuspendedAsync"/>, <see cref="RemoveMemberAsync"/>,
    /// <see cref="SetOwnerAsync{TMember}"/> and <see cref="TransferOwnershipAsync{TMember}"/> read it
    /// precisely because they need current state to decide access. Custom roles have one writer and authorize
    /// nothing on their own.
    /// </remarks>
    public async Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey)
    {
        var cached = await _cache.GetCustomRolesAsync(teamKey);
        if (cached.Found) return cached.Value;

        var team = await GetTeamAsync(teamKey);
        var customRoles = team?.CustomRoles ?? Array.Empty<TenantRoleDefinition>();

        await _cache.SetCustomRolesAsync(teamKey, customRoles);

        return customRoles;
    }

    public async Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        await SetTeamCustomRolesInternalAsync(teamKey, customRoles);
        await _cache.RemoveCustomRolesAsync(teamKey);
        TeamsListChangedEvent?.Invoke(this, new TeamsListChangedEventArgs());
    }

    private async Task<string> GetRandomUnsusedTeamKey()
    {
        string teamKey;
        while (true)
        {
            teamKey = StringExtension.UpperCaseAlphaNumericCharacters.Random();
            if (!await IsTeamKeyInUseAsync(teamKey)) break;
        }

        return teamKey;
    }


    private async Task<IUser> GetCurrentUserAsync()
    {
        var user = await _userService.GetCurrentUserAsync();
        return user;
    }

    private async Task<IUser> RequireCurrentUserAsync()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) throw new UnauthorizedAccessException("Authentication required.");
        return user;
    }

    public static string ResolveDisplayName(IUser user)
    {
        if (user == null) return "Unknown";

        if (!string.IsNullOrEmpty(user.Name))
            return user.Name;

        var email = user.EMail;
        if (string.IsNullOrEmpty(email))
            return "Unknown";

        var atIndex = email.IndexOf('@');
        var username = atIndex >= 0 ? email[..atIndex] : email;
        var words = username.Split('.');
        return string.Join(" ", words.Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }

    private static ITeamMember[] GetMembersFromTeam(ITeam team)
    {
        var membersProperty = team?.GetType().GetProperty("Members");
        return membersProperty?.GetValue(team) as ITeamMember[];
    }
}