using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// <see cref="IUserService.GetTeamMemberUsersAsync"/> resolved through the real container: a caller
/// without the <c>users:manage</c> system scope still sees the users they share a team with, and sees
/// nobody else. Exercised end-to-end because the co-member projection resolves <see cref="ITeamService"/>
/// through a factory — <c>TeamServiceBase</c> depends on <see cref="IUserService"/>, so an eagerly
/// injected team service would close a dependency cycle that only a real provider would expose
/// (Tharga/Team#139).
/// </summary>
public class TeamMemberUserWiringTests
{
    private const string CurrentUserKey = "u-me";
    private const string CoMemberKey = "u-mate";
    private const string StrangerKey = "u-stranger";

    [Fact]
    public async Task TeamMemberUsers_WithoutUsersManage_ReturnsCoMembers()
    {
        using var scope = BuildProvider().CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.GetTeamMemberUsersAsync();

        Assert.Equal([CoMemberKey, CurrentUserKey], result.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TeamMemberUsers_WithoutUsersManage_ExcludesNonCoMembers()
    {
        using var scope = BuildProvider().CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.GetTeamMemberUsersAsync();

        Assert.DoesNotContain(result, x => x.Key == StrangerKey);
    }

    [Fact]
    public async Task FullDirectory_WithoutUsersManage_IsStillDenied()
    {
        using var scope = BuildProvider().CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.GetAsync().ToListAsync());
    }

    [Fact]
    public void TeamService_ResolvesWithoutDependencyCycle()
    {
        using var scope = BuildProvider().CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITeamService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserService>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider());
        services.AddSingleton(Options.Create(new AuditOptions()));
        services.AddSingleton<IAuditLogger>(new NullAuditLogger());
        services.AddSingleton<CompositeAuditLogger>();
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<FakeTeamService, FakeUserService>());
        return services.BuildServiceProvider();
    }

    private sealed class StubAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, CurrentUserKey)], "Test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed class NullAuditLogger : IAuditLogger
    {
        public void Log(AuditEntry entry) { }
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed record TestMember : ITeamMember
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public Invitation Invitation { get; init; }
        public DateTime? LastSeen { get; init; }
        public MembershipState? State { get; init; }
        public AccessLevel? AccessLevel { get; init; }
        public string[] TenantRoles { get; init; }
        public string[] ScopeOverrides { get; init; }
    }

    private sealed record TestTeam : ITeam
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public string Icon { get; init; }
        public ITeamMember[] Members { get; init; }
    }

    private static readonly TestTeam SharedTeam = new()
    {
        Key = "t-1",
        Name = "Shared",
        Members =
        [
            new TestMember { Key = CurrentUserKey, AccessLevel = AccessLevel.Owner },
            new TestMember { Key = CoMemberKey, AccessLevel = AccessLevel.User }
        ]
    };

    private sealed class FakeUserService(AuthenticationStateProvider asp) : UserServiceBase(asp)
    {
        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal)
            => Task.FromResult<IUser>(new TestUser { Key = CurrentUserKey, Identity = CurrentUserKey, EMail = "me@test.com" });

        protected override async IAsyncEnumerable<IUser> GetAllAsync()
        {
            yield return new TestUser { Key = CurrentUserKey, EMail = "me@test.com" };
            yield return new TestUser { Key = CoMemberKey, EMail = "mate@test.com" };
            yield return new TestUser { Key = StrangerKey, EMail = "stranger@test.com" };
            await Task.CompletedTask;
        }
    }

    private sealed class FakeTeamService(IUserService userService) : TeamServiceBase(userService)
    {
        protected override async IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user)
        {
            yield return SharedTeam;
            await Task.CompletedTask;
        }

        protected override Task<ITeam> GetTeamAsync(string teamKey) => Task.FromResult<ITeam>(SharedTeam);

        protected override Task<int> RemoveUserFromAllTeamsInternalAsync(string userKey) => Task.FromResult(0);
        protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName = null) => throw new NotImplementedException();
        protected override Task SetTeamNameAsync(string teamKey, string name) => throw new NotImplementedException();
        protected override Task DeleteTeamAsync(string teamKey) => throw new NotImplementedException();
        protected override Task AddTeamMemberAsync(string teamKey, InviteUserModel model) => throw new NotImplementedException();
        protected override Task RemoveTeamMemberAsync(string teamKey, string userKey) => throw new NotImplementedException();
        protected override Task<ITeam> SetTeamMemberInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept) => throw new NotImplementedException();
        protected override Task SetTeamMemberLastSeenAsync(string teamKey, string userKey) => throw new NotImplementedException();
        protected override Task<ITeamMember> GetTeamMembersAsync(string teamKey, string userKey) => throw new NotImplementedException();
        protected override Task SetTeamMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel) => throw new NotImplementedException();
        protected override Task SetTeamMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles) => throw new NotImplementedException();
        protected override Task SetTeamMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides) => throw new NotImplementedException();
        protected override Task SetTeamMemberNameAsync(string teamKey, string userKey, string name) => throw new NotImplementedException();
        protected override Task SetTeamConsentInternalAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel) => throw new NotImplementedException();
        protected override IAsyncEnumerable<ITeam> GetConsentedTeamsInternalAsync(string[] userRoles) => throw new NotImplementedException();
        protected override Task SetTeamCustomRolesInternalAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => throw new NotImplementedException();
    }
}
