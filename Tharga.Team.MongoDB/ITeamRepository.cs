using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

public interface ITeamRepository<TTeamEntity, TMember> : IRepository
    where TTeamEntity : TeamEntityBase<TMember>
    where TMember : TeamMemberBase
{
    IAsyncEnumerable<TTeamEntity> GetTeamsByUserAsync(string userKey);
    Task<TTeamEntity> GetAsync(string teamKey);
    Task AddAsync(TTeamEntity teamEntity);
    Task DeleteAsync(string teamKey);
    Task RenameAsync(string teamKey, string name);
    Task SetIconAsync(string teamKey, string reference);
    Task SetLastSeenAsync(string teamKey, string userKey, DateTime utcNow);
    Task AddMemberAsync(string teamKey, TMember member);
    Task RemoveMemberAsync(string teamKey, string userKey);
    Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel);
    Task SetMemberSuspendedAsync(string teamKey, string userKey, DateTime? suspendedAt, string suspendedBy);
    Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles);
    Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides);
    Task SetMemberNameAsync(string teamKey, string userKey, string name);
    Task<ITeam> SetInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept);
    Task SetConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null);
    Task SetCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles);
    IAsyncEnumerable<TTeamEntity> GetTeamsByConsentAsync(string[] roles);

    /// <summary>
    /// Every team, regardless of membership — backs the cross-team discovery path authorized by
    /// <see cref="SystemTeamScopes.Read"/>.
    /// </summary>
    /// <remarks>
    /// Declared with a default implementation so existing custom repositories keep compiling. The default
    /// throws rather than returning empty: a silently empty cross-team list is indistinguishable from a
    /// working feature that happens to have nothing to show, which hides the missing implementation.
    /// </remarks>
    IAsyncEnumerable<TTeamEntity> GetAllTeamsAsync()
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(GetAllTeamsAsync)}. Implement it to support " +
            $"cross-team listing (the '{SystemTeamScopes.Read}' system scope).");

    /// <summary>
    /// Marks a team deleted, or clears the mark when <paramref name="deletedAt"/> is null.
    /// </summary>
    /// <remarks>
    /// Declared with a default implementation so existing custom repositories keep compiling — and it
    /// throws rather than no-opping, because a soft delete that silently does nothing reports success while
    /// leaving the team live and readable, which is worse than refusing.
    /// <para>
    /// A repository that does not implement this makes its service report
    /// <c>SupportsSoftDelete = false</c>, so the throw is unreachable through the normal path: the delete
    /// resolves to the irreversible one the store already had.
    /// </para>
    /// </remarks>
    /// <summary>A team by key whether or not it is deleted — for restore, purge and key reservation.</summary>
    Task<TTeamEntity> GetIncludingDeletedAsync(string teamKey)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(GetIncludingDeletedAsync)}. Implement it to " +
            "support soft delete, restore and purge.");

    Task SetDeletedAsync(string teamKey, DateTime? deletedAt, string deletedBy)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(SetDeletedAsync)}. Implement it to support " +
            "soft delete, restore and purge.");

    /// <summary>
    /// Every team including soft-deleted ones — the only read that sees them, for restore and purge
    /// surfaces.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate method rather than a flag on <see cref="GetAllTeamsAsync"/>. A boolean
    /// parameter defaulting to "exclude" is one forgotten argument away from leaking deleted teams into an
    /// ordinary list; a distinct name has to be chosen on purpose.
    /// </remarks>
    IAsyncEnumerable<TTeamEntity> GetAllTeamsIncludingDeletedAsync()
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(GetAllTeamsIncludingDeletedAsync)}. Implement " +
            "it to list soft-deleted teams for restore and purge.");

    /// <summary>
    /// Removes the user's member entries from every team they appear in, regardless of membership state.
    /// Backs user deletion. Returns the number of teams the user was removed from.
    /// </summary>
    /// <remarks>
    /// Declared with a default implementation so existing custom repositories keep compiling. The default
    /// throws rather than returning 0: a silent no-op on a deletion path would leave memberships behind
    /// while reporting success.
    /// </remarks>
    Task<int> RemoveMemberFromAllTeamsAsync(string userKey)
        => throw new NotSupportedException(
            $"'{GetType().Name}' does not implement {nameof(RemoveMemberFromAllTeamsAsync)}. Implement it " +
            $"to support user deletion (the '{SystemUserScopes.Manage}' system scope).");
}