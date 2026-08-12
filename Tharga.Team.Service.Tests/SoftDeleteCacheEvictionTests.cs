namespace Tharga.Team.Service.Tests;

/// <summary>
/// Deleting, restoring or purging a team evicts its members from <see cref="ITeamCache"/>.
/// </summary>
/// <remarks>
/// <b>Without this a soft-deleted team keeps authorizing.</b> <c>TeamServiceBase.GetTeamMemberAsync</c>
/// reads the member cache before the store, so the repository's deleted-team filter is never consulted for
/// a caller whose membership is already cached — they stay authorized on a team that has been deleted,
/// until the entry happens to expire. It is silent, and it is an authorization failure rather than a
/// display one.
/// <para>
/// Found while sweeping the read paths for soft delete, which was shipped on by default in a patch. The
/// storage filter alone looked sufficient and was not.
/// </para>
/// <para>
/// <b>Restore evicts too, for the opposite reason.</b> <c>GetTeamMemberAsync</c> caches a miss as well as a
/// hit, so a lookup made while the team was deleted leaves a cached <c>null</c> that would go on denying
/// access after the team is live again.
/// </para>
/// </remarks>
public class SoftDeleteCacheEvictionTests
{
    private const string TeamKey = "team-1";
    private const string MemberA = "user-a";
    private const string MemberB = "user-b";

    private sealed record TestMember : ITeamMember
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public AccessLevel AccessLevel { get; init; }
        public Invitation Invitation { get; init; }
        public DateTime? LastSeen { get; init; }
        public MembershipState? State { get; init; }
        public string[] TenantRoles { get; init; }
        public string[] ScopeOverrides { get; init; }
    }

    private sealed record TestTeam : ITeam<TestMember>
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public string Icon { get; init; }
        public TestMember[] Members { get; init; }
    }

    /// <summary>Records which member entries were evicted, and whether the team still existed when asked.</summary>
    private sealed class RecordingCache : ITeamCache
    {
        public List<(string TeamKey, string UserKey)> Evicted { get; } = [];

        public Task RemoveMemberAsync(string teamKey, string userKey)
        {
            Evicted.Add((teamKey, userKey));
            return Task.CompletedTask;
        }

        public Task<CachedValue<IUser>> GetUserAsync(string identity) => Task.FromResult(CachedValue<IUser>.Miss);
        public Task SetUserAsync(string identity, IUser user) => Task.CompletedTask;
        public Task RemoveUserAsync(string identity) => Task.CompletedTask;
        public Task RemoveUserByKeyAsync(string userKey) => Task.CompletedTask;
        public Task<CachedValue<ITeamMember>> GetMemberAsync(string teamKey, string userKey) => Task.FromResult(CachedValue<ITeamMember>.Miss);
        public Task SetMemberAsync(string teamKey, string userKey, ITeamMember member) => Task.CompletedTask;
        public Task RemoveMembersForUserAsync(string userKey) => Task.CompletedTask;
        public Task<CachedValue<IReadOnlyList<TenantRoleDefinition>>> GetCustomRolesAsync(string teamKey) => Task.FromResult(CachedValue<IReadOnlyList<TenantRoleDefinition>>.Miss);
        public Task SetCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => Task.CompletedTask;
        public Task RemoveCustomRolesAsync(string teamKey) => Task.CompletedTask;
    }

    /// <summary>
    /// A store that soft-deletes, and that stops returning the team once it is deleted — the behaviour the
    /// real repository filter produces, and the reason the roster must be read before the delete.
    /// </summary>
    private sealed class FakeTeamService(ITeamCache cache) : TeamServiceBase(Substitute.For<IUserService>(), cache: cache)
    {
        private bool _deleted;

        public bool Purged { get; private set; }

        protected override bool SupportsSoftDelete => true;

        protected override Task SoftDeleteTeamAsync(string teamKey, string deletedBy)
        {
            _deleted = true;
            return Task.CompletedTask;
        }

        protected override Task RestoreTeamAsync(string teamKey)
        {
            _deleted = false;
            return Task.CompletedTask;
        }

        protected override Task PurgeTeamAsync(string teamKey)
        {
            Purged = true;
            _deleted = true;
            return Task.CompletedTask;
        }

        protected override Task<ITeam> GetTeamAsync(string teamKey)
            => Task.FromResult<ITeam>(_deleted
                ? null
                : new TestTeam { Key = teamKey, Name = teamKey, Members = [new TestMember { Key = MemberA }, new TestMember { Key = MemberB }] });

        protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => AsyncEnumerable.Empty<ITeam>();
        protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName = null) => Task.FromResult<ITeam>(null);
        protected override Task SetTeamNameAsync(string teamKey, string name) => Task.CompletedTask;
        protected override Task DeleteTeamAsync(string teamKey) { _deleted = true; return Task.CompletedTask; }
        protected override Task AddTeamMemberAsync(string teamKey, InviteUserModel model) => Task.CompletedTask;
        protected override Task RemoveTeamMemberAsync(string teamKey, string userKey) => Task.CompletedTask;
        protected override Task<ITeam> SetTeamMemberInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept) => Task.FromResult<ITeam>(null);
        protected override Task SetTeamMemberLastSeenAsync(string teamKey, string userKey) => Task.CompletedTask;
        protected override Task<ITeamMember> GetTeamMembersAsync(string teamKey, string userKey) => Task.FromResult<ITeamMember>(null);
        protected override Task SetTeamMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel) => Task.CompletedTask;
        protected override Task SetTeamMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles) => Task.CompletedTask;
        protected override Task SetTeamMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides) => Task.CompletedTask;
        protected override Task SetTeamMemberNameAsync(string teamKey, string userKey, string name) => Task.CompletedTask;
        protected override Task SetTeamConsentInternalAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel) => Task.CompletedTask;
        protected override IAsyncEnumerable<ITeam> GetConsentedTeamsInternalAsync(string[] userRoles) => AsyncEnumerable.Empty<ITeam>();
        protected override Task SetTeamCustomRolesInternalAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => Task.CompletedTask;
    }

    /// <summary>The defect: a soft delete that leaves cached memberships behind.</summary>
    [Fact]
    public async Task SoftDelete_EvictsEveryMemberFromTheCache()
    {
        var cache = new RecordingCache();
        var sut = new FakeTeamService(cache);

        await sut.DeleteTeamAsync<TestMember>(TeamKey);

        Assert.Contains((TeamKey, MemberA), cache.Evicted);
        Assert.Contains((TeamKey, MemberB), cache.Evicted);
    }

    /// <summary>
    /// The ordering that makes it work. The roster is read <i>before</i> the delete, because afterwards the
    /// store no longer returns the team — so a version that evicted after reading would evict nothing and
    /// pass every other assertion here.
    /// </summary>
    [Fact]
    public async Task SoftDelete_ReadsTheRosterBeforeDeleting()
    {
        var cache = new RecordingCache();
        var sut = new FakeTeamService(cache);

        await sut.DeleteTeamAsync<TestMember>(TeamKey);

        Assert.Equal(2, cache.Evicted.Count);
    }

    /// <summary>
    /// Restore evicts as well, because a lookup made while the team was deleted cached a <c>null</c> that
    /// would otherwise keep denying access to a live team.
    /// </summary>
    [Fact]
    public async Task Restore_EvictsEveryMemberFromTheCache()
    {
        var cache = new RecordingCache();
        var sut = new FakeTeamService(cache);
        await sut.DeleteTeamAsync<TestMember>(TeamKey);
        cache.Evicted.Clear();

        await sut.RestoreTeamAsync<TestMember>(TeamKey);

        Assert.Contains((TeamKey, MemberA), cache.Evicted);
        Assert.Contains((TeamKey, MemberB), cache.Evicted);
    }

    /// <summary>Purge is a removal too, and leaves the same stale entries if it does not evict.</summary>
    [Fact]
    public async Task Purge_EvictsEveryMemberFromTheCache()
    {
        var cache = new RecordingCache();
        var sut = new FakeTeamService(cache);

        await sut.PurgeTeamAsync<TestMember>(TeamKey);

        Assert.True(sut.Purged);
        Assert.Contains((TeamKey, MemberA), cache.Evicted);
        Assert.Contains((TeamKey, MemberB), cache.Evicted);
    }

    /// <summary>
    /// The self-check. Every assertion above would pass against a cache that recorded an eviction for
    /// every member of every team on any call, so this proves the recorder only sees what it is given.
    /// </summary>
    [Fact]
    public async Task NothingIsEvictedWithoutAnOperation()
    {
        var cache = new RecordingCache();
        _ = new FakeTeamService(cache);

        await Task.CompletedTask;

        Assert.Empty(cache.Evicted);
    }
}
