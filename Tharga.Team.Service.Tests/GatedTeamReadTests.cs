using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// <c>team:read</c> enforcement on <see cref="ITeamManagementService"/>'s reads.
/// </summary>
/// <remarks>
/// <b>These reads were unenforced until now.</b> The interface declared
/// <c>[RequireScope(TeamScopes.Read)]</c> on all four and the implementations were plain pass-throughs —
/// the attributes are inert because the service is not registered through <c>AddTeamService</c>, so no
/// <c>ScopeProxy</c> reads them. A reviewer seeing the attribute would reasonably conclude the read was
/// checked; it was not.
/// <para>
/// The most important test here is <see cref="ViewerLevelMember_IsUnaffected"/>. <c>team:read</c> sits at
/// <see cref="AccessLevel.Viewer"/>, so every ordinary member already holds it and this change is a no-op
/// for them. That is what makes it safe to ship to consumers who are not using
/// <see cref="AccessLevel.Custom"/>.
/// </para>
/// </remarks>
public class GatedTeamReadTests
{
    private sealed record FakeMember(string Key, AccessLevel AccessLevel) : ITeamMember
    {
        public string Name => null;
        public string[] TenantRoles => null;
        public string[] ScopeOverrides => null;
        public MembershipState? State => MembershipState.Member;
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
    }

    private sealed record FakeUser(string Key) : IUser
    {
        public string Identity => "identity";
        public string Name => null;
        public string EMail => null;
        public string DirectoryId => null;
        public string Icon => null;
        public DateTime? LastSeen => null;
    }

    private sealed class FakeUserService(IUser user) : IUserService
    {
        public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => Task.FromResult(user);
        public IAsyncEnumerable<IUser> GetAsync() => AsyncEnumerable.Empty<IUser>();
        public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
        public Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;
    }

    private static ScopeRegistry RegistryGrantingReadFromViewer()
    {
        var registry = new ScopeRegistry();
        registry.Register(TeamScopes.Read, AccessLevel.Viewer, "View team details and members.");
        return registry;
    }

    private static TeamManagementService<FakeMember> Build(AccessLevel? callerLevel, bool useScopes = true, bool authenticated = true)
    {
        var caller = authenticated ? new FakeUser("me") : null;
        var member = callerLevel == null ? null : new FakeMember("me", callerLevel.Value);
        var inner = new StubTeamService(member);

        return useScopes
            ? new TeamManagementService<FakeMember>(inner, new FakeUserService(caller), RegistryGrantingReadFromViewer())
            : new TeamManagementService<FakeMember>(inner);
    }

    /// <summary>
    /// The case that makes this safe to ship. Viewer is where `team:read` is registered, so every
    /// ordinary member already holds it and sees exactly what they saw before.
    /// </summary>
    [Theory]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Administrator)]
    [InlineData(AccessLevel.Owner)]
    public async Task ViewerLevelMember_IsUnaffected(AccessLevel level)
    {
        var sut = Build(level);

        var team = await sut.GetTeamAsync<FakeMember>("team-1");

        Assert.NotNull(team);
    }

    /// <summary>
    /// The hole this closes. `Custom` is documented as carrying only its explicit grants — and read
    /// everything anyway, because nothing checked.
    /// </summary>
    [Fact]
    public async Task CustomLevelCaller_IsRefused()
    {
        var sut = Build(AccessLevel.Custom);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamAsync<FakeMember>("team-1"));
    }

    /// <summary>Every gated read, not just the one the driver happened to use.</summary>
    [Theory]
    [InlineData("team")]
    [InlineData("by-key")]
    [InlineData("members")]
    [InlineData("member")]
    [InlineData("custom-roles")]
    public async Task EveryGatedRead_IsRefusedWithoutTheScope(string operation)
    {
        var sut = Build(AccessLevel.Custom);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            switch (operation)
            {
                case "team": await sut.GetTeamAsync<FakeMember>("team-1"); break;
                case "by-key": await sut.GetTeamByKeyAsync("team-1"); break;
                case "members": await foreach (var _ in sut.GetMembersAsync("team-1")) { } break;
                case "custom-roles": await sut.GetTeamCustomRolesAsync("team-1"); break;
                default: await sut.GetTeamMemberAsync("team-1", "someone"); break;
            }
        });
    }

    /// <summary>A caller who is not a member of the team holds nothing on it.</summary>
    [Fact]
    public async Task NonMember_IsRefused()
    {
        var sut = Build(callerLevel: null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamAsync<FakeMember>("team-1"));
    }

    /// <summary>Fails closed: identity could not be established, so the read is refused.</summary>
    [Fact]
    public async Task UnauthenticatedCaller_IsRefused()
    {
        var sut = Build(AccessLevel.Owner, authenticated: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.GetTeamAsync<FakeMember>("team-1"));
    }

    /// <summary>
    /// The escape that keeps this from breaking apps that never used scopes. No registry and no user
    /// service means the application does not gate anything — enforcing would refuse reads it never
    /// gated. Distinct from a resolved-null caller, which fails closed above.
    /// </summary>
    [Fact]
    public async Task ApplicationNotUsingScopes_IsUnaffected()
    {
        var sut = Build(AccessLevel.Custom, useScopes: false);

        var team = await sut.GetTeamAsync<FakeMember>("team-1");

        Assert.NotNull(team);
    }

    /// <summary>
    /// Minimal <see cref="ITeamService"/>: the reads the gate uses, and throwing on everything else so a
    /// test that starts depending on more fails loudly.
    /// </summary>
    private sealed class StubTeamService(ITeamMember callerMember) : ITeamService
    {
        private sealed record StubTeam(string Key, string Name) : ITeam<FakeMember>
        {
            public string Icon => null;
            public string[] ConsentedRoles => null;
            public AccessLevel? ConsentAccessLevel => null;
            public FakeMember[] Members { get => []; init { } }
        }

        public Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey) => Task.FromResult(callerMember);
        public Task<ITeam<T>> GetTeamAsync<T>(string teamKey) where T : ITeamMember
            => Task.FromResult((ITeam<T>)(object)new StubTeam(teamKey, "Team"));
        public Task<ITeam> GetTeamByKeyAsync(string teamKey) => Task.FromResult<ITeam>(new StubTeam(teamKey, "Team"));
        public IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey) => AsyncEnumerable.Empty<ITeamMember>();

        private static T NotUsed<T>() => throw new NotSupportedException("Not part of the read path under test.");

        public event EventHandler<SelectTeamEventArgs> SelectTeamEvent { add { } remove { } }
        public event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent { add { } remove { } }
        public IAsyncEnumerable<ITeam<T>> GetAllTeamsAsync<T>() where T : ITeamMember => NotUsed<IAsyncEnumerable<ITeam<T>>>();
        public Task<ITeam> CreateTeamAsync(string name = null) => NotUsed<Task<ITeam>>();
        public IAsyncEnumerable<ITeam> GetTeamsAsync() => NotUsed<IAsyncEnumerable<ITeam>>();
        public IAsyncEnumerable<ITeam<T>> GetTeamsAsync<T>() where T : ITeamMember => NotUsed<IAsyncEnumerable<ITeam<T>>>();
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
        public Task SetTeamConsentAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel = null) => NotUsed<Task>();
        public IAsyncEnumerable<ITeam> GetConsentedTeamsAsync(string[] userRoles) => NotUsed<IAsyncEnumerable<ITeam>>();
        public Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey) => Task.FromResult<IReadOnlyList<TenantRoleDefinition>>([]);
        public Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => NotUsed<Task>();
        public Task<int> RemoveUserFromAllTeamsAsync(string userKey) => NotUsed<Task<int>>();
        public Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelAsync(string userKey, AccessLevel accessLevel) => NotUsed<Task<IReadOnlyList<ITeam>>>();
        public Task SetTeamIconAsync(string teamKey, byte[] data, string contentType) => NotUsed<Task>();
        public Task ClearTeamIconAsync(string teamKey) => NotUsed<Task>();
    }
}
