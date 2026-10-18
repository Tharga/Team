using System.Linq.Expressions;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

internal class TeamRepository<TTeamEntity, TMember> : ITeamRepository<TTeamEntity, TMember>
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    private readonly ITeamRepositoryCollection<TTeamEntity, TMember> _collection;

    public TeamRepository(ITeamRepositoryCollection<TTeamEntity, TMember> collection)
    {
        _collection = collection;
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

    /// <summary>Stamps one member's <see cref="TeamMemberBase.LastSeen"/> on a live team; no-op for a non-member.</summary>
    /// <remarks>
    /// <b>One atomic update of that field, never a read-and-replace.</b> This is the most frequent write to a
    /// team document — every active member, every stamp interval (Tharga/Team#317). It used to read the team
    /// and replace the whole document, so a stamp landing between an administrator's read and write could put
    /// back a member they had just removed, suspended or demoted, or undo a rename.
    /// <para>
    /// <b><see cref="EMode.FirstOrDefault"/> is required, not a preference.</b> The positional <c>$</c> needs
    /// the array match from the query that performs the update. In its other modes Tharga.MongoDB finds the
    /// document first and then updates it by id, so the server rejects the update ("The positional operator
    /// did not find the match needed from the query"). <c>FirstOrDefault</c> sends the filter and the update
    /// together as one <c>findAndModify</c>.
    /// </para>
    /// </remarks>
    public Task SetLastSeenAsync(string teamKey, string userKey, DateTime utcNow)
    {
        var filter = LastSeenFilter(teamKey, userKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .Set(x => x.Members.FirstMatchingElement().LastSeen, utcNow);

        return UpdateMemberAsync(filter, update);
    }

    /// <summary>The team, live, with a member of that key; the positional update writes the first such member.</summary>
    internal static FilterDefinition<TTeamEntity> LastSeenFilter(string teamKey, string userKey)
    {
        var builder = new FilterDefinitionBuilder<TTeamEntity>();
        return builder.And(
            builder.Eq(x => x.Key, teamKey),
            builder.Eq(x => x.DeletedAt, null),
            builder.ElemMatch(x => x.Members, m => m.Key == userKey));
    }

    public Task AddMemberAsync(string teamKey, TMember member)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>()
            .Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>()
            .AddToSet(x => x.Members, member);
        return _collection.UpdateOneAsync(filter, update);
    }

    /// <summary>Removes every row of that member from the team; no-op when the team or the member is missing.</summary>
    /// <remarks>
    /// A <c>$pull</c>, not the remaining members written back, so a change to another member saved meanwhile
    /// survives (Tharga/Team#338). Dropping every row with the key is what the read-and-filter version did.
    /// </remarks>
    public Task RemoveMemberAsync(string teamKey, string userKey)
    {
        return UpdateMemberAsync(TeamFilter(teamKey), PullMember(userKey));
    }

    public Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel)
    {
        var update = Builders<TTeamEntity>.Update
            .Set(x => x.Members.FirstMatchingElement().AccessLevel, accessLevel);

        return UpdateMemberAsync(MemberFilter(teamKey, userKey), update);
    }

    /// <remarks>
    /// <b>Two matches resolve to null rather than to the first.</b> The code is a bearer credential, so
    /// picking one of two teams would admit somebody to a team nobody invited them to. Collisions should be
    /// impossible at 72 bits — this is what makes "should be" safe to rely on.
    /// </remarks>
    public async Task<TTeamEntity> GetByInviteKeyAsync(string inviteKey)
    {
        if (string.IsNullOrWhiteSpace(inviteKey)) return null;

        var matches = await _collection
            .GetAsync(x => x.DeletedAt == null && x.Members.Any(y => y.Invitation.InviteKey == inviteKey))
            .Take(2)
            .ToArrayAsync();

        return matches.Length == 1 ? matches[0] : null;
    }

    public Task SetInvitationExpiryAsync(string teamKey, string inviteKey, DateTime? expiresAt)
    {
        // Only the expiry is written, so InviteKey -- and with it every link already mailed -- is untouched.
        var update = Builders<TTeamEntity>.Update
            .Set(x => x.Members.FirstMatchingElement().Invitation.ExpiresAt, expiresAt);

        return UpdateMemberAsync(InvitationFilter(teamKey, inviteKey), update);
    }

    public Task SetMemberSuspendedAsync(string teamKey, string userKey, DateTime? suspendedAt, string suspendedBy)
    {
        // State is deliberately left at Member. GetTeamsByUserAsync filters on it, so changing it here
        // would drop the team out of the member's selector -- the opposite of what suspension is for.
        var update = Builders<TTeamEntity>.Update.Combine(
            SetOrUnset(x => x.Members.FirstMatchingElement().SuspendedAt, suspendedAt),
            SetOrUnset(x => x.Members.FirstMatchingElement().SuspendedBy, suspendedBy));

        return UpdateMemberAsync(MemberFilter(teamKey, userKey), update);
    }

    public Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles)
    {
        var update = SetOrUnset(x => x.Members.FirstMatchingElement().TenantRoles, tenantRoles);
        return UpdateMemberAsync(MemberFilter(teamKey, userKey), update);
    }

    public Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides)
    {
        var update = SetOrUnset(x => x.Members.FirstMatchingElement().ScopeOverrides, scopeOverrides);
        return UpdateMemberAsync(MemberFilter(teamKey, userKey), update);
    }

    public Task SetMemberNameAsync(string teamKey, string userKey, string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var update = Builders<TTeamEntity>.Update
            .Set(x => x.Members.FirstMatchingElement().Name, trimmed);

        return UpdateMemberAsync(MemberFilter(teamKey, userKey), update);
    }

    /// <summary>
    /// Records the answer on the invitation's row and returns the team as it is afterwards, or null when no
    /// invitation of that code is on the team.
    /// </summary>
    public async Task<ITeam> SetInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept)
    {
        var update = Builders<TTeamEntity>.Update
            .Set(x => x.Members.FirstMatchingElement().Key, userKey)
            .Set(x => x.Members.FirstMatchingElement().LastSeen, DateTime.UtcNow)
            .Set(x => x.Members.FirstMatchingElement().State, accept ? MembershipState.Member : MembershipState.Rejected);

        if (accept)
        {
            update = update
                .Set(x => x.Members.FirstMatchingElement().Name, null)
                .Set(x => x.Members.FirstMatchingElement().Invitation, null);
        }

        var result = await UpdateMemberAsync(InvitationFilter(teamKey, inviteKey), update);
        return result?.Before == null ? null : await result.GetAfterAsync();
    }

    public async Task<int> RemoveMemberFromAllTeamsAsync(string userKey)
    {
        var count = 0;
        await foreach (var team in _collection.GetAsync(x => x.Members.Any(y => y.Key == userKey)))
        {
            await UpdateMemberAsync(TeamFilter(team.Key), PullMember(userKey));
            count++;
        }

        return count;
    }

    /// <summary>
    /// Every member write goes through here: one update of only what changes, never the member list read and
    /// written back, so two overlapping writes to one team both survive (Tharga/Team#338). Before, an edit to
    /// one member could put back a member another administrator had just removed, or undo a suspension.
    /// </summary>
    /// <remarks>
    /// <see cref="EMode.FirstOrDefault"/> is required by the positional writes, for the reason given on
    /// <see cref="SetLastSeenAsync"/>, and keeps each write to one <c>findAndModify</c>.
    /// </remarks>
    private Task<EntityChangeResult<TTeamEntity>> UpdateMemberAsync(FilterDefinition<TTeamEntity> filter, UpdateDefinition<TTeamEntity> update)
    {
        return _collection.UpdateOneAsync(filter, update, OneOption<TTeamEntity>.FirstOrDefault);
    }

    private static FilterDefinition<TTeamEntity> TeamFilter(string teamKey)
    {
        return Builders<TTeamEntity>.Filter.Eq(x => x.Key, teamKey);
    }

    /// <summary>The team with a member of that key; a positional update writes the first such member.</summary>
    internal static FilterDefinition<TTeamEntity> MemberFilter(string teamKey, string userKey)
    {
        var builder = Builders<TTeamEntity>.Filter;
        return builder.And(
            builder.Eq(x => x.Key, teamKey),
            builder.ElemMatch(x => x.Members, m => m.Key == userKey));
    }

    /// <summary>The team with an invitation of that code; a positional update writes the first such member.</summary>
    internal static FilterDefinition<TTeamEntity> InvitationFilter(string teamKey, string inviteKey)
    {
        var builder = Builders<TTeamEntity>.Filter;
        return builder.And(
            builder.Eq(x => x.Key, teamKey),
            builder.ElemMatch(x => x.Members, m => m.Invitation != null && m.Invitation.InviteKey == inviteKey));
    }

    private static UpdateDefinition<TTeamEntity> PullMember(string userKey)
    {
        return Builders<TTeamEntity>.Update.PullFilter(x => x.Members, m => m.Key == userKey);
    }

    /// <summary>Sets the field, or removes it when the value is null.</summary>
    /// <remarks>
    /// For member fields marked <c>[BsonIgnoreIfNull]</c>. Writing the whole member left a null one absent rather
    /// than stored as null, and readers rely on the difference: an access level that was never set is found with
    /// <c>$exists: false</c>. A positional write keeps the same shape.
    /// </remarks>
    private static UpdateDefinition<TTeamEntity> SetOrUnset<TField>(Expression<Func<TTeamEntity, TField>> field, TField value)
    {
        return value == null
            ? Builders<TTeamEntity>.Update.Unset(new ExpressionFieldDefinition<TTeamEntity>(field))
            : Builders<TTeamEntity>.Update.Set(field, value);
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
            .Set(x => x.ConsentAccessLevel, accessLevel)
            .Unset(x => x.TemporaryConsent);
        // A manager setting consent directly makes it standing: any time-bound window from an approved request ends.
        return _collection.UpdateOneAsync(filter, update);
    }

    /// <remarks>
    /// Pushed rather than written back as a whole array, so a request filed while another is being decided cannot
    /// overwrite that decision. The requester's earlier pending requests are cancelled first, one conditional update
    /// each; there is normally at most one.
    /// </remarks>
    public async Task AddAccessRequestAsync(string teamKey, TeamAccessRequestEntity request)
    {
        for (var attempt = 0; attempt < TeamAccessRequestRules.HistoryLimit; attempt++)
        {
            var pendingFromRequester = Builders<TTeamEntity>.Filter.And(
                Builders<TTeamEntity>.Filter.Eq(x => x.Key, teamKey),
                Builders<TTeamEntity>.Filter.ElemMatch(x => x.AccessRequests, Builders<TeamAccessRequestEntity>.Filter.And(
                    Builders<TeamAccessRequestEntity>.Filter.Eq(r => r.RequesterKey, request.RequesterKey),
                    Builders<TeamAccessRequestEntity>.Filter.Eq(r => r.Status, TeamAccessRequestStatus.Pending))));

            var cancel = Builders<TTeamEntity>.Update
                .Set(x => x.AccessRequests.FirstMatchingElement().Status, TeamAccessRequestStatus.Cancelled)
                .Set(x => x.AccessRequests.FirstMatchingElement().DecidedBy, request.RequesterKey)
                .Set(x => x.AccessRequests.FirstMatchingElement().DecidedAt, request.RequestedAt);

            var cancelled = await _collection.UpdateOneAsync(pendingFromRequester, cancel, OneOption<TTeamEntity>.FirstOrDefault);
            if (cancelled?.Before == null) break;
        }

        var filter = Builders<TTeamEntity>.Filter.Eq(x => x.Key, teamKey);
        var push = Builders<TTeamEntity>.Update.PushEach(
            x => x.AccessRequests,
            [request],
            slice: TeamAccessRequestRules.HistoryLimit,
            sort: Builders<TeamAccessRequestEntity>.Sort.Descending(r => r.RequestedAt));

        await _collection.UpdateOneAsync(filter, push);
    }

    public async Task<bool> DecideAccessRequestAsync(string teamKey, string requestId, TeamAccessRequestStatus status, string decidedBy, DateTime decidedAt)
    {
        var update = Builders<TTeamEntity>.Update
            .Set(x => x.AccessRequests.FirstMatchingElement().Status, status)
            .Set(x => x.AccessRequests.FirstMatchingElement().DecidedBy, decidedBy)
            .Set(x => x.AccessRequests.FirstMatchingElement().DecidedAt, decidedAt);

        var result = await _collection.UpdateOneAsync(PendingRequest(teamKey, requestId), update, OneOption<TTeamEntity>.FirstOrDefault);
        return result?.Before != null;
    }

    /// <remarks>
    /// One update: the request's status and the team's consent are one fact, so neither can land without the other.
    /// Conditional on the request still being pending, so two managers approving at once cannot both write.
    /// </remarks>
    public async Task<bool> ApproveAccessRequestAsync(string teamKey, string requestId, string decidedBy, DateTime decidedAt, DateTime? grantedUntil,
        string[] consentedRoles, AccessLevel accessLevel, TemporaryConsentEntity temporaryConsent)
    {
        var update = Builders<TTeamEntity>.Update
            .Set(x => x.AccessRequests.FirstMatchingElement().Status, TeamAccessRequestStatus.Approved)
            .Set(x => x.AccessRequests.FirstMatchingElement().DecidedBy, decidedBy)
            .Set(x => x.AccessRequests.FirstMatchingElement().DecidedAt, decidedAt)
            .Set(x => x.AccessRequests.FirstMatchingElement().GrantedUntil, grantedUntil)
            .Set(x => x.ConsentedRoles, consentedRoles)
            .Set(x => x.ConsentAccessLevel, accessLevel);

        update = temporaryConsent == null
            ? update.Unset(x => x.TemporaryConsent)
            : update.Set(x => x.TemporaryConsent, temporaryConsent);

        var result = await _collection.UpdateOneAsync(PendingRequest(teamKey, requestId), update, OneOption<TTeamEntity>.FirstOrDefault);
        return result?.Before != null;
    }

    /// <remarks>
    /// Every write using this filter must pass <see cref="OneOption{TEntity}.FirstOrDefault"/>. It is the only mode
    /// that sends this filter to the server as the update's own filter; the others find the document first and then
    /// update it by <c>_id</c> alone, which drops the array condition — so the positional <c>$</c> operator has
    /// nothing to resolve and the server rejects the command, and the "still pending" condition stops being atomic.
    /// </remarks>
    private static FilterDefinition<TTeamEntity> PendingRequest(string teamKey, string requestId)
        => Builders<TTeamEntity>.Filter.And(
            Builders<TTeamEntity>.Filter.Eq(x => x.Key, teamKey),
            Builders<TTeamEntity>.Filter.ElemMatch(x => x.AccessRequests, Builders<TeamAccessRequestEntity>.Filter.And(
                Builders<TeamAccessRequestEntity>.Filter.Eq(r => r.Id, requestId),
                Builders<TeamAccessRequestEntity>.Filter.Eq(r => r.Status, TeamAccessRequestStatus.Pending))));

    public Task SetCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles)
    {
        var filter = new FilterDefinitionBuilder<TTeamEntity>().Eq(x => x.Key, teamKey);
        var update = new UpdateDefinitionBuilder<TTeamEntity>().Set(x => x.CustomRoles, customRoles);
        return _collection.UpdateOneAsync(filter, update);
    }

    public IAsyncEnumerable<TTeamEntity> GetTeamsByConsentAsync(string[] roles)
    {
        // Also matches the consent a temporary consent returns to: after expiry that is the consent in force, and a
        // role it covers may not be among the stored roles. TeamServiceBase filters the result by what is in force.
        return _collection.GetAsync(x => x.DeletedAt == null
                                         && ((x.ConsentedRoles != null && x.ConsentedRoles.Any(r => roles.Contains(r)))
                                             || (x.TemporaryConsent != null && x.TemporaryConsent.PreviousConsentedRoles != null && x.TemporaryConsent.PreviousConsentedRoles.Any(r => roles.Contains(r)))));
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
