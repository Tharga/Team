namespace Tharga.Team.Service.Tests;

/// <summary>
/// A soft-deleted team keeps its key reserved until it is purged.
/// </summary>
/// <remarks>
/// <b>Without this, deleting a team hands its key to the next one created.</b> Key generation asks whether
/// a team holds the key, and that read now excludes soft-deleted teams — so a deleted team's key reads as
/// free. In a deployment that derives a team's database name from its key (Eplicta FortDocs runs
/// <c>DatabasePart = teamKey</c>), the new team is then pointed at the deleted team's data.
/// <para>
/// That is the corruption Tharga/Team#224 is about, reached by a route the issue did not anticipate: not a
/// failed drop, but a successful create. It was introduced by soft delete rather than found by it, which is
/// why the read-path sweep had to cover writes that <i>consult</i> a read, not only reads themselves.
/// </para>
/// <para>
/// The existing comment in <c>TeamCustomRolesCacheTests</c> — <i>"a deleted team's key is handed out
/// again"</i> — described this as a fact about hard delete, where it was harmless because nothing survived
/// to collide with. Soft delete made it dangerous.
/// </para>
/// </remarks>
public class SoftDeletedKeyReservationTests
{
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

    /// <summary>
    /// A store holding one soft-deleted team. The live read hides it, exactly as the filtered repository
    /// read does; the key check is the thing under test.
    /// </summary>
    private sealed class FakeTeamService : TeamServiceBase
    {
        private readonly string _deletedTeamKey;

        public FakeTeamService(string deletedTeamKey)
            : base(Substitute.For<IUserService>())
        {
            _deletedTeamKey = deletedTeamKey;
        }

        protected override bool SupportsSoftDelete => true;

        /// <summary>Live teams only — a soft-deleted team is invisible here.</summary>
        protected override Task<ITeam> GetTeamAsync(string teamKey) => Task.FromResult<ITeam>(null);

        /// <summary>Sees the deleted team, which is the point.</summary>
        protected override Task<bool> IsTeamKeyInUseAsync(string teamKey)
            => Task.FromResult(teamKey == _deletedTeamKey);

        public Task<bool> IsInUse(string teamKey) => IsTeamKeyInUseAsync(teamKey);

        protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => AsyncEnumerable.Empty<ITeam>();
        protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName = null)
            => Task.FromResult<ITeam>(new TestTeam { Key = teamKey, Name = name, Members = [] });
        protected override Task SetTeamNameAsync(string teamKey, string name) => Task.CompletedTask;
        protected override Task DeleteTeamAsync(string teamKey) => Task.CompletedTask;
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

    /// <summary>A soft-deleted team's key is still taken.</summary>
    [Fact]
    public async Task ASoftDeletedTeamKey_IsStillInUse()
    {
        var sut = new FakeTeamService("DELETED-KEY");

        Assert.True(await sut.IsInUse("DELETED-KEY"));
    }

    /// <summary>The self-check: a key nobody holds is free, or the assertion above proves nothing.</summary>
    [Fact]
    public async Task AnUnusedKey_IsFree()
    {
        var sut = new FakeTeamService("DELETED-KEY");

        Assert.False(await sut.IsInUse("SOME-OTHER-KEY"));
    }

    /// <summary>
    /// The default implementation keeps the old behaviour — live teams only — so a store that cannot see
    /// deleted teams is unaffected. That is also the store that cannot soft-delete, so it never has one.
    /// </summary>
    [Fact]
    public async Task TheDefaultFallsBackToTheLiveRead()
    {
        var sut = new DefaultKeyCheckService();

        Assert.False(await sut.IsInUse("ANY-KEY"));
        Assert.True(sut.LiveReadWasConsulted);
    }

    private sealed class DefaultKeyCheckService : TeamServiceBase
    {
        public DefaultKeyCheckService() : base(Substitute.For<IUserService>()) { }

        public bool LiveReadWasConsulted { get; private set; }

        public Task<bool> IsInUse(string teamKey) => IsTeamKeyInUseAsync(teamKey);

        protected override Task<ITeam> GetTeamAsync(string teamKey)
        {
            LiveReadWasConsulted = true;
            return Task.FromResult<ITeam>(null);
        }

        protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => AsyncEnumerable.Empty<ITeam>();
        protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName = null) => Task.FromResult<ITeam>(null);
        protected override Task SetTeamNameAsync(string teamKey, string name) => Task.CompletedTask;
        protected override Task DeleteTeamAsync(string teamKey) => Task.CompletedTask;
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
}
