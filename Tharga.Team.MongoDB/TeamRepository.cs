using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

internal class TeamRepository<TTeamEntity, TMember> : ITeamRepository<TTeamEntity, TMember>
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    private readonly ITeamRepositoryCollection<TTeamEntity, TMember> _collection;
    private readonly ILogger<TeamRepository<TTeamEntity, TMember>> _logger;

    public TeamRepository(ITeamRepositoryCollection<TTeamEntity, TMember> collection, ILogger<TeamRepository<TTeamEntity, TMember>> logger = null)
    {
        _collection = collection;
        _logger = logger;
    }

    public IAsyncEnumerable<TTeamEntity> GetTeamsByUserAsync(string userKey)
    {
        return _collection.GetAsync(x => x.DeletedAt == null && x.Members.Any(y => y.Key == userKey && y.State == MembershipState.Member));
    }

    /// <summary>
    /// A live team by key. Returns null for a soft-deleted one, which is what makes a deleted team stop
    /// resolving and stop granting access everywhere at once.
    /// </summary>
    public Task<TTeamEntity> GetAsync(string teamKey)
    {
        return _collection.GetOneAsync(x => x.Key == teamKey && x.DeletedAt == null);
    }

    /// <summary>
    /// A team by key <b>whether or not it is deleted</b> — for restore, purge, and for refusing to reuse a
    /// deleted team's key.
    /// </summary>
    /// <remarks>
    /// Named rather than parameterised, for the same reason as
    /// <see cref="GetAllTeamsIncludingDeletedAsync"/>: a defaulted boolean is one forgotten argument away
    /// from resurrecting a deleted team into an ordinary read.
    /// </remarks>
    public Task<TTeamEntity> GetIncludingDeletedAsync(string teamKey)
    {
        return _collection.GetOneAsync(x => x.Key == teamKey);
    }

    public Task AddAsync(TTeamEntity teamEntity)
    {
        return _collection.AddAsync(teamEntity);
    }

    public async Task SetLastSeenAsync(string teamKey, string userKey, DateTime utcNow)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>().Eq(x => x.Key, teamKey);
        var team = await _collection.GetOneAsync(filter);

        var target = team.Members.PickOneOrDefault(x => x.Key == userKey, _logger, teamKey, userKey);
        if (target == null) return;

        var updated = target with { LastSeen = utcNow };
        team = team with { Members = team.Members.ReplaceByReference(target, updated) };
        await _collection.ReplaceOneAsync(team);
    }

    public Task AddMemberAsync(string teamKey, TMember member)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .AddToSet(x => x.Members, member);
        return _collection.UpdateOneAsync(filter, update);
    }

    public async Task RemoveMemberAsync(string teamKey, string userKey)
    {
        var team = await _collection.GetOneAsync(x => x.Key == teamKey);
        var members = team.Members.Where(x => x.Key != userKey).ToArray();

        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members, members);

        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel)
    {
        var team = await _collection.GetOneAsync(x => x.Key == teamKey);

        var target = team.Members.PickOneOrDefault(x => x.Key == userKey, _logger, teamKey, userKey);
        if (target == null) return;

        var updated = target with { AccessLevel = accessLevel };
        var members = team.Members.ReplaceByReference(target, updated);

        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members, members);

        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task SetInvitationExpiryAsync(string teamKey, string inviteKey, DateTime? expiresAt)
    {
        var team = await _collection.GetOneAsync(x => x.Key == teamKey);

        var target = team?.Members.PickOneOrDefault(x => x.Invitation != null && x.Invitation.InviteKey == inviteKey, _logger, teamKey, inviteKey);
        if (target == null) return;

        // The invitation is replaced whole rather than the expiry set in place, because Invitation is a
        // record with required members -- and InviteKey is copied across unchanged, which is the property
        // that makes an already-mailed link survive an extension.
        var updated = target with { Invitation = target.Invitation with { ExpiresAt = expiresAt } };
        var members = team.Members.ReplaceByReference(target, updated);

        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members, members);

        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task SetMemberSuspendedAsync(string teamKey, string userKey, DateTime? suspendedAt, string suspendedBy)
    {
        var team = await _collection.GetOneAsync(x => x.Key == teamKey);

        var target = team.Members.PickOneOrDefault(x => x.Key == userKey, _logger, teamKey, userKey);
        if (target == null) return;

        // State is deliberately left at Member. GetTeamsByUserAsync filters on it, so changing it here
        // would drop the team out of the member's selector -- the opposite of what suspension is for.
        var updated = target with { SuspendedAt = suspendedAt, SuspendedBy = suspendedBy };
        var members = team.Members.ReplaceByReference(target, updated);

        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members, members);

        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles)
    {
        var team = await _collection.GetOneAsync(x => x.Key == teamKey);

        var target = team.Members.PickOneOrDefault(x => x.Key == userKey, _logger, teamKey, userKey);
        if (target == null) return;

        var updated = target with { TenantRoles = tenantRoles };
        var members = team.Members.ReplaceByReference(target, updated);

        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members, members);

        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides)
    {
        var team = await _collection.GetOneAsync(x => x.Key == teamKey);

        var target = team.Members.PickOneOrDefault(x => x.Key == userKey, _logger, teamKey, userKey);
        if (target == null) return;

        var updated = target with { ScopeOverrides = scopeOverrides };
        var members = team.Members.ReplaceByReference(target, updated);

        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members, members);

        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task SetMemberNameAsync(string teamKey, string userKey, string name)
    {
        var team = await _collection.GetOneAsync(x => x.Key == teamKey);

        var target = team.Members.PickOneOrDefault(x => x.Key == userKey, _logger, teamKey, userKey);
        if (target == null) return;

        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var updated = target with { Name = trimmed };
        var members = team.Members.ReplaceByReference(target, updated);

        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members, members);

        await _collection.UpdateOneAsync(filter, update);
    }

    public async Task<ITeam> SetInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept)
    {
        var team = await _collection.GetOneAsync(x => x.Key == teamKey);

        var target = team.Members.PickOneOrDefault(x => x.Invitation != null && x.Invitation.InviteKey == inviteKey, _logger, teamKey, inviteKey);
        if (target == null) return null;

        TMember updated;
        if (accept)
        {
            updated = target with
            {
                Key = userKey,
                Name = null,
                Invitation = null,
                LastSeen = DateTime.UtcNow,
                State = MembershipState.Member
            };
        }
        else
        {
            updated = target with
            {
                Key = userKey,
                LastSeen = DateTime.UtcNow,
                State = MembershipState.Rejected
            };
        }

        var members = team.Members.ReplaceByReference(target, updated);

        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members, members);

        var response = await _collection.UpdateOneAsync(filter, update);
        return await response.GetAfterAsync();
    }

    public async Task<int> RemoveMemberFromAllTeamsAsync(string userKey)
    {
        var count = 0;
        await foreach (var team in _collection.GetAsync(x => x.Members.Any(y => y.Key == userKey)))
        {
            var members = team.Members.Where(x => x.Key != userKey).ToArray();

            var filter = new FilterDefinitionBuilder<TTeamEntity>()
                .Eq(x => x.Key, team.Key);
            var update = new UpdateDefinitionBuilder<TTeamEntity>()
                .Set(x => x.Members, members);

            await _collection.UpdateOneAsync(filter, update);
            count++;
        }

        return count;
    }

    public Task DeleteAsync(string teamKey)
    {
        return _collection.DeleteOneAsync(x => x.Key == teamKey);
    }

    public Task RenameAsync(string teamKey, string name)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>().Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>().Set(x => x.Name, name);
        return _collection.UpdateOneAsync(filter, update);
    }

    public Task SetIconAsync(string teamKey, string reference)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>().Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>().Set(x => x.Icon, reference);
        return _collection.UpdateOneAsync(filter, update);
    }

    public Task SetConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>().Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.ConsentedRoles, consentedRoles)
            .Set(x => x.ConsentAccessLevel, accessLevel);
        return _collection.UpdateOneAsync(filter, update);
    }

    public Task SetCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>().Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>().Set(x => x.CustomRoles, customRoles);
        return _collection.UpdateOneAsync(filter, update);
    }

    public IAsyncEnumerable<TTeamEntity> GetTeamsByConsentAsync(string[] roles)
    {
        return _collection.GetAsync(x => x.DeletedAt == null && x.ConsentedRoles != null && x.ConsentedRoles.Any(r => roles.Contains(r)));
    }

    /// <summary>
    /// Every live team. Soft-deleted teams are excluded here, not by the caller.
    /// </summary>
    /// <remarks>
    /// <b>The filter belongs at the store, not above it.</b> Every caller excluding deleted teams for
    /// itself is a rule restated once per read, and the read that forgets is a silent leak rather than a
    /// visible bug. <see cref="GetAllTeamsIncludingDeletedAsync"/> is the one way to see them, and has to
    /// be asked for by name.
    /// </remarks>
    public IAsyncEnumerable<TTeamEntity> GetAllTeamsAsync()
    {
        return _collection.GetAsync(x => x.DeletedAt == null);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TTeamEntity> GetAllTeamsIncludingDeletedAsync()
    {
        return _collection.GetAsync(x => true);
    }

    /// <inheritdoc />
    public Task SetDeletedAsync(string teamKey, DateTime? deletedAt, string deletedBy)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>().Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.DeletedAt, deletedAt)
            .Set(x => x.DeletedBy, deletedAt == null ? null : deletedBy);
        return _collection.UpdateOneAsync(filter, update);
    }

}
