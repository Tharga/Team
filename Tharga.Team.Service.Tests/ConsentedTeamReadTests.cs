using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Reads gated by <c>team:read</c> reaching the same answer as the mutations beside them: through
/// <c>TeamGrantResolver</c>, so consent and tenant roles count.
/// </summary>
/// <remarks>
/// <b>Regression for Tharga/Team#248.</b> The gate used to recompute the caller's scopes from their member
/// row. Mutations never did — <c>AuthorizationTeamServiceDecorator</c> reads claims, and claims already
/// carry consent-derived scopes because <c>TeamGrantResolver</c> issued them. So the two halves of one
/// operation disagreed: a consent-based operator could invite a member and then be refused the read that
/// followed, leaving the invite created and the operator told "Access denied".
/// <para>
/// <see cref="MemberWhoseTenantRoleGrantsRead_CanRead"/> covers the second defect the same drift caused,
/// which nobody reported: the old gate called <c>IScopeRegistry.GetEffectiveScopes</c> directly, where the
/// resolver prefers <c>ITenantRoleService.GetEffectiveScopesAsync</c>. A per-team custom role granting
/// <c>team:read</c> was therefore honoured when claims were built and ignored when a read was gated.
/// </para>
/// <para>
/// <see cref="GetTeamsAsync_AgreesWithTheSingleTeamGate"/> is the one that keeps them honest. The list
/// filter and the single-team gate are separate call sites, and them drifting apart is the whole cause of
/// this issue — an assertion that they answer alike is cheaper than discovering it again from a stack trace.
/// </para>
/// </remarks>
public class ConsentedTeamReadTests
{
    private const string TeamKey = "team-1";
    private const string ConsentedRole = "Support";
    private const string ReaderRole = "reader";

    private sealed record FakeMember(
        string Key,
        AccessLevel AccessLevel,
        string[] TenantRoles = null,
        DateTime? SuspendedAt = null) : ITeamMember
    {
        public string Name => null;
        public string[] ScopeOverrides => null;
        public MembershipState? State => MembershipState.Member;
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
    }

    private sealed record FakeUser(string Key) : IUser
    {
        public string Identity => "identity";
        public string EMail => null;
    }

    private sealed class FakeUserService(IUser user) : IUserService
    {
        public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => Task.FromResult(user);
        public IAsyncEnumerable<IUser> GetAsync() => AsyncEnumerable.Empty<IUser>();
        public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
        public Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    /// <summary>Resolves <see cref="ReaderRole"/> to <c>team:read</c> and nothing else to anything.</summary>
    private sealed class StubTenantRoleService : ITenantRoleService
    {
        public Task<IReadOnlyList<TenantRoleDefinition>> GetRolesAsync(string teamKey)
            => Task.FromResult<IReadOnlyList<TenantRoleDefinition>>([]);

        public Task<IReadOnlyList<string>> GetEffectiveScopesAsync(
            string teamKey, AccessLevel accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides = null)
        {
            var scopes = roleNames != null && roleNames.Contains(ReaderRole)
                ? (IReadOnlyList<string>)[TeamScopes.Read]
                : [];

            return Task.FromResult(scopes);
        }
    }

    private static ClaimsPrincipal PrincipalHolding(params string[] roles)
        => new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    private static ScopeRegistry RegistryGrantingReadFromViewer()
    {
        var registry = new ScopeRegistry();
        registry.Register(TeamScopes.Read, AccessLevel.Viewer, "View team details and members.");
        return registry;
    }

    private static TeamManagementService<FakeMember> Build(
        FakeMember member = null,
        string[] consentedRoles = null,
        AccessLevel? consentAccessLevel = null,
        string[] callerRoles = null,
        ITenantRoleService tenantRoleService = null,
        AccessLevel defaultConsentLevel = AccessLevel.Viewer)
    {
        var inner = new StubTeamService(member, consentedRoles, consentAccessLevel);

        return new TeamManagementService<FakeMember>(
            inner,
            new FakeUserService(new FakeUser("me")),
            RegistryGrantingReadFromViewer(),
            new FixedPrincipalAccessor(PrincipalHolding(callerRoles ?? [])),
            tenantRoleService,
            Microsoft.Extensions.Options.Options.Create(new ConsentOptions { AccessLevel = defaultConsentLevel }));
    }

    /// <summary>
    /// The defect. Not a member, but the team consented to a role the caller holds — the same grant that
    /// let them perform the write immediately before.
    /// </summary>
    [Fact]
    public async Task ConsentedNonMember_CanReadTheTeam()
    {
        var sut = Build(member: null, consentedRoles: [ConsentedRole], callerRoles: [ConsentedRole]);

        var team = await sut.GetTeamAsync<FakeMember>(TeamKey);

        Assert.NotNull(team);
    }

    /// <summary>Every gated read, not just the one the reporter's stack trace happened to name.</summary>
    [Theory]
    [InlineData("team")]
    [InlineData("by-key")]
    [InlineData("members")]
    [InlineData("member")]
    [InlineData("custom-roles")]
    public async Task EveryGatedRead_IsAllowedForAConsentedNonMember(string operation)
    {
        var sut = Build(member: null, consentedRoles: [ConsentedRole], callerRoles: [ConsentedRole]);

        switch (operation)
        {
            case "team": Assert.NotNull(await sut.GetTeamAsync<FakeMember>(TeamKey)); break;
            case "by-key": Assert.NotNull(await sut.GetTeamByKeyAsync(TeamKey)); break;
            case "members": await foreach (var _ in sut.GetMembersAsync(TeamKey)) { } break;
            case "custom-roles": Assert.NotNull(await sut.GetTeamCustomRolesAsync(TeamKey)); break;
            default: await sut.GetTeamMemberAsync(TeamKey, "someone"); break;
        }
    }

    /// <summary>The team's own statement still decides: consenting to nothing grants nothing.</summary>
    [Fact]
    public async Task TeamConsentingToNothing_StillRefuses()
    {
        var sut = Build(member: null, consentedRoles: null, callerRoles: [ConsentedRole]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamAsync<FakeMember>(TeamKey));
    }

    /// <summary>Consent is to a named role, not to everyone.</summary>
    [Fact]
    public async Task NonMemberWithoutTheConsentedRole_IsRefused()
    {
        var sut = Build(member: null, consentedRoles: [ConsentedRole], callerRoles: ["Unrelated"]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamAsync<FakeMember>(TeamKey));
    }

    /// <summary>
    /// The level the team consented at decides what the grant carries. <see cref="AccessLevel.Custom"/>
    /// carries only its explicit grants, so it does not reach <c>team:read</c>.
    /// </summary>
    [Fact]
    public async Task ConsentAtALevelBelowRead_IsRefused()
    {
        var sut = Build(
            member: null,
            consentedRoles: [ConsentedRole],
            consentAccessLevel: AccessLevel.Custom,
            callerRoles: [ConsentedRole]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamAsync<FakeMember>(TeamKey));
    }

    /// <summary>
    /// A team that consented without naming a level falls back to the configured default, so a host
    /// configuring <c>Consent.AccessLevel</c> gets what it configured.
    /// </summary>
    [Fact]
    public async Task ConsentWithoutALevel_UsesTheConfiguredDefault()
    {
        var refused = Build(
            member: null, consentedRoles: [ConsentedRole], callerRoles: [ConsentedRole],
            defaultConsentLevel: AccessLevel.Custom);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refused.GetTeamAsync<FakeMember>(TeamKey));

        var allowed = Build(
            member: null, consentedRoles: [ConsentedRole], callerRoles: [ConsentedRole],
            defaultConsentLevel: AccessLevel.Administrator);

        Assert.NotNull(await allowed.GetTeamAsync<FakeMember>(TeamKey));
    }

    /// <summary>
    /// The second defect, unreported. The old gate never consulted <see cref="ITenantRoleService"/>, so a
    /// per-team custom role granting <c>team:read</c> was honoured when claims were built and ignored here.
    /// </summary>
    [Fact]
    public async Task MemberWhoseTenantRoleGrantsRead_CanRead()
    {
        var member = new FakeMember("me", AccessLevel.Custom, TenantRoles: [ReaderRole]);
        var sut = Build(member: member, tenantRoleService: new StubTenantRoleService());

        var team = await sut.GetTeamAsync<FakeMember>(TeamKey);

        Assert.NotNull(team);
    }

    /// <summary>And a custom-level member holding no such role is still refused.</summary>
    [Fact]
    public async Task MemberWithoutAGrantingTenantRole_IsRefused()
    {
        var member = new FakeMember("me", AccessLevel.Custom, TenantRoles: ["unrelated"]);
        var sut = Build(member: member, tenantRoleService: new StubTenantRoleService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamAsync<FakeMember>(TeamKey));
    }

    /// <summary>
    /// Suspension wins over everything, and over consent in particular — otherwise suspending a member of a
    /// consenting team would quietly leave their access intact.
    /// </summary>
    /// <remarks>
    /// <b>A third defect the same drift caused, and the one worth reading twice.</b> This test fails against
    /// the old gate: it checked the member's effective scopes and never looked at <c>SuspendedAt</c> at all,
    /// so a suspended Owner kept full read access to the team's details and roster. <c>TeamGrantResolver</c>
    /// has always refused a suspended member — the gate simply was not asking it. Unlike the other two this
    /// one *granted* access rather than refusing it, which is why it never produced a support report.
    /// </remarks>
    [Fact]
    public async Task SuspendedMember_IsStillRefused()
    {
        var member = new FakeMember("me", AccessLevel.Owner, SuspendedAt: DateTime.UtcNow);
        var sut = Build(member: member, consentedRoles: [ConsentedRole], callerRoles: [ConsentedRole]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamAsync<FakeMember>(TeamKey));
    }

    /// <summary>
    /// The list filter and the single-team gate must agree. They are separate call sites, and drifting
    /// apart is exactly what caused this issue.
    /// </summary>
    [Theory]
    [InlineData(AccessLevel.Viewer, true)]
    [InlineData(AccessLevel.Owner, true)]
    [InlineData(AccessLevel.Custom, false)]
    public async Task GetTeamsAsync_AgreesWithTheSingleTeamGate(AccessLevel level, bool expected)
    {
        var member = new FakeMember("me", level);
        var sut = Build(member: member);

        var listed = await sut.GetTeamsAsync<FakeMember>().AnyAsync();

        var gateAllows = true;
        try
        {
            await sut.GetTeamAsync<FakeMember>(TeamKey);
        }
        catch (UnauthorizedAccessException)
        {
            gateAllows = false;
        }

        Assert.Equal(expected, listed);
        Assert.Equal(expected, gateAllows);
    }

    /// <summary>
    /// Minimal <see cref="ITeamService"/>: the reads the gate uses plus the consent lookup, throwing on
    /// everything else so a test that starts depending on more fails loudly.
    /// </summary>
    private sealed class StubTeamService(
        ITeamMember callerMember,
        string[] consentedRoles,
        AccessLevel? consentAccessLevel) : ITeamService
    {
        private sealed record StubTeam(string Key, string Name, string[] ConsentedRoles, AccessLevel? ConsentAccessLevel)
            : ITeam<FakeMember>
        {
            public string Icon => null;
            public FakeMember[] Members { get; init; } = [];
        }

        private StubTeam Team => new(TeamKey, "Team", consentedRoles, consentAccessLevel)
        {
            Members = callerMember == null ? [] : [(FakeMember)callerMember]
        };

        public Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey) => Task.FromResult(callerMember);
        public Task<ITeam<T>> GetTeamAsync<T>(string teamKey) where T : ITeamMember
            => Task.FromResult((ITeam<T>)(object)Team);
        public Task<ITeam> GetTeamByKeyAsync(string teamKey) => Task.FromResult<ITeam>(Team);
        public IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey) => AsyncEnumerable.Empty<ITeamMember>();
        public Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey)
            => Task.FromResult<IReadOnlyList<TenantRoleDefinition>>([]);

        public IAsyncEnumerable<ITeam<T>> GetTeamsAsync<T>() where T : ITeamMember
            => new[] { (ITeam<T>)(object)Team }.ToAsyncEnumerable();

        public IAsyncEnumerable<ITeam> GetConsentedTeamsAsync(string[] userRoles)
        {
            var consented = consentedRoles != null && userRoles.Intersect(consentedRoles).Any();
            return consented ? new ITeam[] { Team }.ToAsyncEnumerable() : AsyncEnumerable.Empty<ITeam>();
        }

        private static T NotUsed<T>() => throw new NotSupportedException("Not part of the read path under test.");

        public event EventHandler<SelectTeamEventArgs> SelectTeamEvent { add { } remove { } }
        public event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent { add { } remove { } }
        public IAsyncEnumerable<ITeam<T>> GetAllTeamsAsync<T>() where T : ITeamMember => NotUsed<IAsyncEnumerable<ITeam<T>>>();
        public Task<ITeam> CreateTeamAsync(string name = null) => NotUsed<Task<ITeam>>();
        public IAsyncEnumerable<ITeam> GetTeamsAsync() => NotUsed<IAsyncEnumerable<ITeam>>();
        public IAsyncEnumerable<ITeam> GetAllTeamsAsync() => NotUsed<IAsyncEnumerable<ITeam>>();
        public Task RenameTeamAsync<T>(string teamKey, string name) where T : ITeamMember => NotUsed<Task>();
        public Task DeleteTeamAsync<T>(string teamKey) where T : ITeamMember => NotUsed<Task>();
        public Task AddMemberAsync(string teamKey, InviteUserModel model) => NotUsed<Task>();
        public Task RemoveMemberAsync(string teamKey, string userKey) => NotUsed<Task>();
        public Task SetMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel) => NotUsed<Task>();
        public Task SetMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles) => NotUsed<Task>();
        public Task SetMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides) => NotUsed<Task>();
        public Task SetMemberNameAsync(string teamKey, string userKey, string name) => NotUsed<Task>();
        public Task SetInvitationResponseAsync(string teamKey, string userKey, string inviteCode, bool accept) => NotUsed<Task>();
        public Task SetMemberLastSeenAsync(string teamKey) => NotUsed<Task>();
        public Task TransferOwnershipAsync<T>(string teamKey, string newOwnerUserKey) where T : ITeamMember => NotUsed<Task>();
        public Task<SetOwnerResult> SetOwnerAsync<T>(string teamKey, string newOwnerUserKey) where T : ITeamMember => NotUsed<Task<SetOwnerResult>>();
        public Task SetTeamConsentAsync(string teamKey, string[] consented, AccessLevel? accessLevel = null) => NotUsed<Task>();
        public Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => NotUsed<Task>();
        public Task<int> RemoveUserFromAllTeamsAsync(string userKey) => NotUsed<Task<int>>();
        public Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelAsync(string userKey, AccessLevel accessLevel) => NotUsed<Task<IReadOnlyList<ITeam>>>();
        public Task SetTeamIconAsync(string teamKey, byte[] data, string contentType) => NotUsed<Task>();
        public Task ClearTeamIconAsync(string teamKey) => NotUsed<Task>();
    }
}
