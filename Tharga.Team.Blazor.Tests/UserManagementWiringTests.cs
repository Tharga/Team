using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Wiring of <see cref="IUserManagementService"/> by AddThargaTeamBlazor / AddThargaTeam:
/// authorization sits outermost over audit over the implementation, the <c>users:manage</c> system
/// scope is registered merge-safe, a delete through the fully-resolved chain emits both the user-level
/// and the team-level audit entries, and directory features degrade to <see cref="NotSupportedException"/>
/// when no <see cref="IUserDirectoryService"/> is registered.
/// </summary>
public class UserManagementWiringTests
{
    [Fact]
    public void UserManagement_AuthorizationDecorator_IsOutermost()
    {
        var (sp, _) = BuildProvider(withScope: true);
        using var scope = sp.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

        Assert.IsType<AuthorizationUserManagementServiceDecorator>(service);
    }

    [Fact]
    public void UserService_AuthorizationDecorator_IsOutermost()
    {
        var (sp, _) = BuildProvider(withScope: true);
        using var scope = sp.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        Assert.IsType<AuthorizationUserServiceDecorator>(service);
    }

    [Fact]
    public async Task UserByKey_WithoutScope_IsDenied_ThroughTheGatedService_ButReadable_ThroughTheUndecoratedOne()
    {
        var (sp, _) = BuildProvider(withScope: false);
        using var scope = sp.CreateScope();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => scope.ServiceProvider.GetRequiredService<IUserService>().GetUserByKeyAsync("user-1"));

        var inner = scope.ServiceProvider.GetRequiredKeyedService<IUserService>(UserServiceKeys.Inner);
        Assert.IsNotType<AuthorizationUserServiceDecorator>(inner);
        Assert.Equal("user-1", (await inner.GetUserByKeyAsync("user-1")).Key);
    }

    [Fact]
    public async Task UserEnumeration_WithoutScope_IsDenied()
    {
        var (sp, _) = BuildProvider(withScope: false);
        using var scope = sp.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.GetAsync().ToListAsync());
    }

    [Fact]
    public async Task CurrentUserResolve_WithoutScope_PassesThrough()
    {
        var (sp, _) = BuildProvider(withScope: false);
        using var scope = sp.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var user = await service.GetCurrentUserAsync();

        Assert.Null(user);
    }

    [Fact]
    public void UsersManageSystemScope_IsRegistered()
    {
        var (sp, _) = BuildProvider(withScope: true);

        var registry = sp.GetRequiredService<ISystemScopeRegistry>();

        Assert.Contains(registry.All, s => s.Name == SystemUserScopes.Manage);
    }

    [Fact]
    public async Task Delete_WithoutScope_IsDenied()
    {
        var (sp, recorder) = BuildProvider(withScope: false);
        using var scope = sp.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteUserAsync("u-1"));
        Assert.Empty(recorder.Entries);
    }

    [Fact]
    public async Task Delete_ThroughWiredChain_EmitsUserAndTeamAuditEntries()
    {
        var (sp, recorder) = BuildProvider(withScope: true);
        using var scope = sp.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

        var result = await service.DeleteUserAsync("u-1");

        Assert.Equal(2, result.RemovedTeamCount);
        Assert.Contains(recorder.Entries, e => e.Feature == "user" && e.Action == "delete");
        Assert.Contains(recorder.Entries, e => e.Feature == "team" && e.Action == "remove-member-all");
    }

    [Fact]
    public async Task Verify_WithoutDirectoryService_ThrowsNotSupported()
    {
        var (sp, _) = BuildProvider(withScope: true);
        using var scope = sp.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserManagementService>();

        await Assert.ThrowsAsync<NotSupportedException>(() => service.VerifyUserAsync("u-1"));
    }

    [Fact]
    public void AddUserDirectoryService_RegistersCustomProvider()
    {
        var builder = WebApplication.CreateBuilder();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            { "AzureAd": { "Authority": "https://test.ciamlogin.com/test", "ClientId": "c", "TenantId": "t", "CallbackPath": "/signin-oidc" } }
            """));
        builder.Configuration.AddJsonStream(stream);

        builder.AddThargaTeam(o => o.AddUserDirectoryService<FakeDirectoryService>());

        Assert.Contains(builder.Services, d =>
            d.ServiceType == typeof(IUserDirectoryService) && d.ImplementationType == typeof(FakeDirectoryService));
    }

    private static (ServiceProvider Provider, RecordingAuditLogger Recorder) BuildProvider(bool withScope)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(withScope));

        var recorder = new RecordingAuditLogger();
        services.AddSingleton(Options.Create(new AuditOptions()));
        services.AddSingleton<IAuditLogger>(recorder);
        services.AddSingleton<CompositeAuditLogger>();

        services.AddThargaTeamBlazor(o => o.RegisterTeamService<FakeTeamService, FakeUserService>());

        return (services.BuildServiceProvider(), recorder);
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public readonly List<AuditEntry> Entries = [];
        public void Log(AuditEntry entry) => Entries.Add(entry);
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class StubAuthStateProvider(bool withScope) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var claims = new List<Claim>();
            if (withScope) claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemUserScopes.Manage));
            var identity = new ClaimsIdentity(claims, authenticationType: "Test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed class FakeUserService(AuthenticationStateProvider asp) : UserServiceBase(asp)
    {
        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal) => Task.FromResult<IUser>(null);
        protected override async IAsyncEnumerable<IUser> GetAllAsync() { yield break; }
        public override Task<IUser> GetUserByKeyAsync(string userKey)
            => Task.FromResult<IUser>(new TestUser { Key = userKey, Identity = $"id-{userKey}" });
        public override Task DeleteUserAsync(string userKey) => Task.CompletedTask;
    }

    private sealed class FakeDirectoryService : IUserDirectoryService
    {
        public Task<DirectoryVerificationResult> VerifyUserAsync(IUser user, CancellationToken cancellationToken = default)
            => Task.FromResult(new DirectoryVerificationResult(DirectoryUserStatus.Found));
        public Task DeleteUserAsync(string directoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async IAsyncEnumerable<DirectoryUser> GetUsersAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }

    private sealed class FakeTeamService(IUserService userService) : TeamServiceBase(userService)
    {
        protected override Task<int> RemoveUserFromAllTeamsInternalAsync(string userKey) => Task.FromResult(2);

        protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => throw new NotImplementedException();
        protected override Task<ITeam> GetTeamAsync(string teamKey) => throw new NotImplementedException();
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
