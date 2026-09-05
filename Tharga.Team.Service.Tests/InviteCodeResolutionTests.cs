using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Resolving an invitation link, in both the form links take now and the form they took before.
/// </summary>
/// <remarks>
/// From Tharga/Team#249. A link used to carry base64 <c>{TeamKey, Code}</c> — base64 being an encoding and
/// not encryption, so the team key was readable by anything the link passed through. It now carries a short
/// opaque token and the store resolves it.
/// <para>
/// <b>The older form has to keep working, and that is not a courtesy.</b> Those links are sitting unopened
/// in inboxes; dropping them would invalidate every invitation already sent, silently, at upgrade.
/// </para>
/// </remarks>
public class InviteCodeResolutionTests
{
    private const string TeamKey = "team-1";
    private const string Token = "Zm9vYmFyYmF6cXV4MTIzNA";
    private const string EMail = "invitee@example.com";

    private sealed record FakeMember(string Key, AccessLevel AccessLevel, Invitation Invitation) : ITeamMember
    {
        public string Name => null;
        public string[] TenantRoles => null;
        public string[] ScopeOverrides => null;
        public MembershipState? State => MembershipState.Invited;
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

    /// <summary>Old-format link: base64 of the JSON payload, exactly as links minted before 3.20 carry it.</summary>
    private static string LegacyCode(string teamKey, string code)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new InviteModel { TeamKey = teamKey, Code = code })));

    private static TeamManagementService<FakeMember> Build(bool storeResolvesTokens = true)
        => new(new StubTeamService(storeResolvesTokens), new FakeUserService(new FakeUser("me")), null);

    [Fact]
    public async Task AShortToken_ResolvesToItsTeam()
    {
        var invitation = await Build().GetInvitationAsync(Token);

        Assert.NotNull(invitation);
        Assert.Equal(TeamKey, invitation.TeamKey);
        Assert.Equal(EMail, invitation.EMail);
    }

    /// <summary>So the acceptance screen never decodes the link a second time.</summary>
    [Fact]
    public async Task TheResolvedInvitation_CarriesTheCodeToRespondWith()
    {
        var invitation = await Build().GetInvitationAsync(Token);

        Assert.Equal(Token, invitation.InviteKey);
    }

    [Fact]
    public async Task AnOlderLink_StillResolves()
    {
        var invitation = await Build().GetInvitationAsync(LegacyCode(TeamKey, Token));

        Assert.NotNull(invitation);
        Assert.Equal(TeamKey, invitation.TeamKey);
        Assert.Equal(Token, invitation.InviteKey);
    }

    /// <summary>
    /// A store that has not implemented the token lookup keeps working on links that name their team — which
    /// is the whole point of the seam defaulting to null instead of throwing.
    /// </summary>
    [Fact]
    public async Task AStoreThatCannotResolveTokens_StillResolvesOlderLinks()
    {
        var sut = Build(storeResolvesTokens: false);

        Assert.NotNull(await sut.GetInvitationAsync(LegacyCode(TeamKey, Token)));
        Assert.Null(await sut.GetInvitationAsync(Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token-at-all")]
    [InlineData("!!!!not base64!!!!")]
    public async Task AnythingThatNamesNoInvitation_ResolvesToNull(string code)
    {
        Assert.Null(await Build().GetInvitationAsync(code));
    }

    /// <summary>An old-format link naming a team that has no such code resolves to nothing, not to the team.</summary>
    [Fact]
    public async Task AnOlderLinkWithAnUnknownCode_ResolvesToNull()
    {
        Assert.Null(await Build().GetInvitationAsync(LegacyCode(TeamKey, "some-other-code")));
    }

    /// <summary>
    /// Minimal store: it knows one team holding one invitation, and can be built without the ability to
    /// resolve a bare token so the fallback path is actually executed rather than merely written.
    /// </summary>
    private sealed class StubTeamService(bool resolvesTokens) : ITeamService
    {
        private static readonly Invitation TheInvitation = new()
        {
            EMail = EMail,
            InviteKey = Token,
            InviteTime = DateTime.UtcNow
        };

        private sealed record StubTeam(string Key, string Name) : ITeam<FakeMember>
        {
            public string Icon => null;
            public string[] ConsentedRoles => null;
            public AccessLevel? ConsentAccessLevel => null;
            public FakeMember[] Members { get; init; } = [new("member-key", AccessLevel.User, TheInvitation)];
        }

        public Task<string> GetTeamKeyByInviteKeyAsync(string inviteKey)
            => Task.FromResult(resolvesTokens && inviteKey == Token ? TeamKey : null);

        public Task<ITeam<T>> GetTeamAsync<T>(string teamKey) where T : ITeamMember
            => Task.FromResult(teamKey == TeamKey ? (ITeam<T>)(object)new StubTeam(TeamKey, "Test Team") : null);

        private static T NotUsed<T>() => throw new NotSupportedException("Not part of the resolve path under test.");

        public event EventHandler<SelectTeamEventArgs> SelectTeamEvent { add { } remove { } }
        public event EventHandler<TeamsListChangedEventArgs> TeamsListChangedEvent { add { } remove { } }
        public Task<ITeamMember> GetTeamMemberAsync(string teamKey, string userKey) => Task.FromResult<ITeamMember>(null);
        public Task<ITeam> GetTeamByKeyAsync(string teamKey) => NotUsed<Task<ITeam>>();
        public IAsyncEnumerable<ITeamMember> GetMembersAsync(string teamKey) => AsyncEnumerable.Empty<ITeamMember>();
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
        public Task SetTeamConsentAsync(string teamKey, string[] consented, AccessLevel? accessLevel = null) => NotUsed<Task>();
        public IAsyncEnumerable<ITeam> GetConsentedTeamsAsync(string[] userRoles) => NotUsed<IAsyncEnumerable<ITeam>>();
        public Task<IReadOnlyList<TenantRoleDefinition>> GetTeamCustomRolesAsync(string teamKey) => NotUsed<Task<IReadOnlyList<TenantRoleDefinition>>>();
        public Task SetTeamCustomRolesAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => NotUsed<Task>();
        public Task<int> RemoveUserFromAllTeamsAsync(string userKey) => NotUsed<Task<int>>();
        public Task<IReadOnlyList<ITeam>> GetTeamsForUserWithAccessLevelAsync(string userKey, AccessLevel accessLevel) => NotUsed<Task<IReadOnlyList<ITeam>>>();
        public Task SetTeamIconAsync(string teamKey, byte[] data, string contentType) => NotUsed<Task>();
        public Task ClearTeamIconAsync(string teamKey) => NotUsed<Task>();
    }
}
