using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

public abstract class TeamServiceRepositoryBase<TTeamEntity, TMember> : TeamServiceBase
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    private readonly ITeamRepository<TTeamEntity, TMember> _teamRepository;
    private readonly IMongoDbServiceFactory _mongoDbServiceFactory;

    /// <param name="userService">Resolves the calling user.</param>
    /// <param name="teamRepository">The team collection this service reads and writes.</param>
    /// <param name="mongoDbServiceFactory">Used for the operations that go outside the repository.</param>
    /// <param name="iconStore">Optional. Required only for team icons.</param>
    /// <param name="cache">
    /// Optional. <b>Forward it from your own service's constructor</b> when running more than one instance —
    /// left unforwarded, the claims-path lookups fall back to a process-local cache that cannot see another
    /// instance's writes. See <see cref="ITeamCache"/>.
    /// </param>
    protected TeamServiceRepositoryBase(IUserService userService, ITeamRepository<TTeamEntity, TMember> teamRepository, IMongoDbServiceFactory mongoDbServiceFactory, IIconStore iconStore = null, ITeamCache cache = null)
        : base(userService, iconStore: iconStore, cache: cache)
    {
        _teamRepository = teamRepository;
        _mongoDbServiceFactory = mongoDbServiceFactory;
    }

    protected abstract Task<TTeamEntity> CreateTeam(string teamKey, string name, IUser user, string displayName);
    protected abstract Task<TMember> CreateTeamMember(InviteUserModel model);

    protected override async Task<ITeam> GetTeamAsync(string teamKey)
    {
        return await _teamRepository.GetAsync(teamKey);
    }

    protected override async Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName)
    {
        var team = await CreateTeam(teamKey, name, user, displayName);

        await _teamRepository.AddAsync(team);

        return team;
    }

    protected override Task SetTeamNameAsync(string teamKey, string name)
    {
        return _teamRepository.RenameAsync(teamKey, name);
    }

    /// <summary>
    /// A key is in use while <b>any</b> team holds it, deleted or not, so a soft-deleted team keeps its key
    /// reserved until it is purged.
    /// </summary>
    protected override async Task<bool> IsTeamKeyInUseAsync(string teamKey)
        => await _teamRepository.GetIncludingDeletedAsync(teamKey) != null;

    /// <summary>This store can soft-delete, so <c>TeamDeleteMode.Soft</c> takes effect.</summary>
    protected override bool SupportsSoftDelete => true;

    /// <inheritdoc />
    protected override Task SoftDeleteTeamAsync(string teamKey, string deletedBy)
    {
        return _teamRepository.SetDeletedAsync(teamKey, DateTime.UtcNow, deletedBy);
    }

    /// <inheritdoc />
    protected override Task RestoreTeamAsync(string teamKey)
    {
        return _teamRepository.SetDeletedAsync(teamKey, null, null);
    }

    /// <summary>
    /// Removes the team record and drops its database. The only path here that needs <c>dropDatabase</c>.
    /// </summary>
    /// <remarks>
    /// Same ordering and same wrapping as <see cref="DeleteTeamAsync(string)"/> — record first, so a drop
    /// failure leaves an orphaned database rather than a live team pointing at nothing.
    /// </remarks>
    protected override async Task PurgeTeamAsync(string teamKey)
    {
        await _teamRepository.DeleteAsync(teamKey);

        await DropTeamDatabaseAsync(teamKey);
    }

    /// <summary>
    /// Removes the team record, then drops the team's database.
    /// </summary>
    /// <remarks>
    /// <b>The record goes first, and the order is the point.</b> The two writes cannot be made atomic —
    /// one is a document delete, the other a database drop — so the only choice is which way a partial
    /// failure fails. Dropping first and then failing to delete the record leaves a <i>live team pointing
    /// at deleted data</i>: it still lists, still resolves, still authorizes, and every read against it
    /// returns empty. Deleting the record first leaves an orphaned database, which is inert and which a
    /// sweep can find. Reported by Eplicta FortDocs (Tharga/Team#224), where the drop happened to throw
    /// first and nothing was lost.
    /// <para>
    /// A drop failure is wrapped as <see cref="TeamStorageException"/> rather than allowed to surface as a
    /// driver exception — see that type for why the message addresses the deployment rather than the caller.
    /// The record is already gone at that point, which is the intended outcome: the team is deleted, and
    /// what remains is a database to clean up.
    /// </para>
    /// </remarks>
    protected override async Task DeleteTeamAsync(string teamKey)
    {
        await _teamRepository.DeleteAsync(teamKey);

        await DropTeamDatabaseAsync(teamKey);
    }

    /// <summary>
    /// Drops the database backing <paramref name="teamKey"/>, translating a store refusal into
    /// <see cref="TeamStorageException"/>.
    /// </summary>
    private Task DropTeamDatabaseAsync(string teamKey)
    {
        try
        {
            var databaseContext = new DatabaseContext { DatabasePart = teamKey };
            var service = _mongoDbServiceFactory.GetMongoDbService(() => databaseContext);
            var databaseName = service.GetDatabaseName();
            service.DropDatabase(databaseName);
        }
        catch (Exception e)
        {
            throw new TeamStorageException(teamKey,
                $"The team record for '{teamKey}' was removed, but its database could not be dropped. " +
                "This deployment's database user is not permitted to drop databases — in MongoDB Atlas, " +
                "'readWriteAnyDatabase' does not include 'dropDatabase'; it needs 'dbAdminAnyDatabase', " +
                "'atlasAdmin' or a custom role covering every per-team database. The database is now " +
                "orphaned and can be dropped manually.", e);
        }

        return Task.CompletedTask;
    }

    protected override async Task AddTeamMemberAsync(string teamKey, InviteUserModel model)
    {
        var memberModel = await CreateTeamMember(model);

        // Auto-generate Member.Key if not set by the consumer (typical for invited members
        // that don't yet correspond to a User document)
        if (string.IsNullOrEmpty(memberModel.Key))
        {
            memberModel = memberModel with { Key = Guid.NewGuid().ToString() };
        }

        // Auto-generate Invitation if not set by the consumer
        if (memberModel.Invitation == null && !string.IsNullOrEmpty(model.Email))
        {
            memberModel = memberModel with
            {
                Invitation = new Invitation
                {
                    EMail = model.Email,
                    InviteKey = Guid.NewGuid().ToString(),
                    InviteTime = DateTime.UtcNow
                }
            };
        }

        // Default state to Invited if not set
        if (memberModel.State == null)
        {
            memberModel = memberModel with { State = MembershipState.Invited };
        }

        await _teamRepository.AddMemberAsync(teamKey, memberModel);
    }

    protected override Task RemoveTeamMemberAsync(string teamKey, string userKey)
    {
        return _teamRepository.RemoveMemberAsync(teamKey, userKey);
    }

    protected override Task<ITeam> SetTeamMemberInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept)
    {
        return _teamRepository.SetInvitationResponseAsync(teamKey, userKey, inviteKey, accept);
    }

    protected override async Task<string> GetInvitedMemberNameAsync(string teamKey, string inviteKey)
    {
        var team = await _teamRepository.GetAsync(teamKey);
        var member = team?.Members.FirstOrDefault(x => x.Invitation != null && x.Invitation.InviteKey == inviteKey);
        return member?.Name;
    }

    protected override Task SetTeamMemberLastSeenAsync(string teamKey, string userKey)
    {
        return _teamRepository.SetLastSeenAsync(teamKey, userKey, DateTime.UtcNow);
    }

    protected override async Task<ITeamMember> GetTeamMembersAsync(string teamKey, string userKey)
    {
        var team = await _teamRepository.GetTeamsByUserAsync(userKey).FirstOrDefaultAsync(x => x.Key == teamKey);
        return team?.Members.FirstOrDefault(x => x.Key == userKey);
    }

    protected override Task SetTeamMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel)
    {
        return _teamRepository.SetMemberRoleAsync(teamKey, userKey, accessLevel);
    }

    protected override Task SetTeamMemberSuspendedAsync(string teamKey, string userKey, DateTime? suspendedAt, string suspendedBy)
    {
        return _teamRepository.SetMemberSuspendedAsync(teamKey, userKey, suspendedAt, suspendedBy);
    }

    protected override Task SetTeamMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles)
    {
        return _teamRepository.SetMemberTenantRolesAsync(teamKey, userKey, tenantRoles);
    }

    protected override Task SetTeamMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides)
    {
        return _teamRepository.SetMemberScopeOverridesAsync(teamKey, userKey, scopeOverrides);
    }

    protected override Task SetTeamMemberNameAsync(string teamKey, string userKey, string name)
    {
        return _teamRepository.SetMemberNameAsync(teamKey, userKey, name);
    }

    protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user)
    {
        if (user == null) return AsyncEnumerable.Empty<ITeam>();
        return _teamRepository.GetTeamsByUserAsync(user.Key);
    }

    protected override IAsyncEnumerable<ITeam> GetAllTeamsInternalAsync()
    {
        return _teamRepository.GetAllTeamsAsync();
    }

    protected override Task<int> RemoveUserFromAllTeamsInternalAsync(string userKey)
    {
        return _teamRepository.RemoveMemberFromAllTeamsAsync(userKey);
    }

    /// <remarks>
    /// Filters the cross-team enumeration rather than adding a repository query. The set is bounded by
    /// the number of teams, this runs once per delete confirmation rather than per request, and a
    /// dedicated query would be a second place for the membership shape to be interpreted.
    /// </remarks>
    protected override async Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelInternalAsync(string userKey, AccessLevel accessLevel)
    {
        if (string.IsNullOrEmpty(userKey)) return [];

        var matches = new List<ITeam>();
        await foreach (var team in _teamRepository.GetAllTeamsAsync())
        {
            if (team?.Members?.Any(m => m != null && m.Key == userKey && m.AccessLevel == accessLevel) == true)
                matches.Add(team);
        }

        return matches;
    }

    protected override Task SetTeamIconReferenceInternalAsync(string teamKey, string reference)
    {
        return _teamRepository.SetIconAsync(teamKey, reference);
    }

    protected override Task SetTeamConsentInternalAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel)
    {
        return _teamRepository.SetConsentAsync(teamKey, consentedRoles, accessLevel);
    }

    protected override Task SetTeamCustomRolesInternalAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        return _teamRepository.SetCustomRolesAsync(teamKey, customRoles);
    }

    protected override IAsyncEnumerable<ITeam> GetConsentedTeamsInternalAsync(string[] userRoles)
    {
        return _teamRepository.GetTeamsByConsentAsync(userRoles);
    }
}