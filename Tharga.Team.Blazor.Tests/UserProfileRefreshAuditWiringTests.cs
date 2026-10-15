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
/// A profile refreshed from a trusted sign-in reaches the audit log through the registration, the same way a
/// first-sign-in user creation does, and a failing audit never fails the resolve that triggered it.
/// </summary>
public class UserProfileRefreshAuditWiringTests
{
    [Fact]
    public async Task ARefreshedProfile_IsAudited()
    {
        await using var provider = BuildProvider(new RecordingAuditLogger(), out var recorder);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IUserService>().GetCurrentUserAsync(Principal());

        var entry = Assert.Single(recorder.Entries, e => e.Action == AuthAuditEntries.UserProfileRefreshedAction);
        Assert.Equal("name,email", entry.Metadata["changed"]);
    }

    [Fact]
    public async Task AFailingAudit_DoesNotFailTheResolve()
    {
        await using var provider = BuildProvider(new ThrowingAuditLogger(), out _);
        using var scope = provider.CreateScope();

        var user = await scope.ServiceProvider.GetRequiredService<IUserService>().GetCurrentUserAsync(Principal());

        Assert.NotNull(user);
    }

    private static ClaimsPrincipal Principal()
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, $"sub-{Guid.NewGuid():N}"),
            new Claim("name", "Anna Svensson"),
            new Claim("email", "anna.svensson@example.com")
        ], "Cookies"));

    private static ServiceProvider BuildProvider(IAuditLogger auditLogger, out RecordingAuditLogger recorder)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider());

        recorder = auditLogger as RecordingAuditLogger;
        services.AddSingleton(Options.Create(new AuditOptions()));
        services.AddSingleton(auditLogger);
        services.AddSingleton<CompositeAuditLogger>();

        services.AddThargaTeamBlazor(o => o.RegisterTeamService<FakeTeamService, RefreshingUserService>());

        return services.BuildServiceProvider();
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public readonly List<AuditEntry> Entries = [];
        public void Log(AuditEntry entry) => Entries.Add(entry);
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class ThrowingAuditLogger : IAuditLogger
    {
        public void Log(AuditEntry entry) => throw new InvalidOperationException("audit store down");
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class StubAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(Principal()));
    }

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
        public string Name { get; init; }
    }

    private sealed class RefreshingUserService(AuthenticationStateProvider asp) : UserServiceBase(asp, cache: new InMemoryTeamCache())
    {
        protected override TimeSpan? LastSeenStampInterval => null;

        protected override bool ShouldRefreshProfile(ClaimsPrincipal claimsPrincipal) => true;

        protected override Task SetUserProfileAsync(string userKey, string name, string email) => Task.CompletedTask;

        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal)
        {
            var identity = ResolveUserIdentity(claimsPrincipal);
            return Task.FromResult<IUser>(new TestUser { Key = $"u-{identity}", Identity = identity, Name = "Anna Andersson", EMail = "anna@example.com" });
        }

        protected override async IAsyncEnumerable<IUser> GetAllAsync() { yield break; }
    }

    private sealed class FakeTeamService(IUserService userService) : TeamServiceBase(userService)
    {
        protected override Task<ITeamMember> GetTeamMembersAsync(string teamKey, string userKey) => Task.FromResult<ITeamMember>(null);
        protected override Task<ITeam> GetTeamAsync(string teamKey) => Task.FromResult<ITeam>(null);
        protected override IAsyncEnumerable<ITeam> GetConsentedTeamsInternalAsync(string[] userRoles) => AsyncEnumerable.Empty<ITeam>();

        protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => throw new NotImplementedException();
        protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName = null) => throw new NotImplementedException();
        protected override Task SetTeamNameAsync(string teamKey, string name) => throw new NotImplementedException();
        protected override Task DeleteTeamAsync(string teamKey) => throw new NotImplementedException();
        protected override Task AddTeamMemberAsync(string teamKey, InviteUserModel model) => throw new NotImplementedException();
        protected override Task RemoveTeamMemberAsync(string teamKey, string userKey) => throw new NotImplementedException();
        protected override Task<ITeam> SetTeamMemberInvitationResponseAsync(string teamKey, string userKey, string inviteKey, bool accept) => throw new NotImplementedException();
        protected override Task SetTeamMemberLastSeenAsync(string teamKey, string userKey) => throw new NotImplementedException();
        protected override Task SetTeamMemberRoleAsync(string teamKey, string userKey, AccessLevel accessLevel) => throw new NotImplementedException();
        protected override Task SetTeamMemberTenantRolesAsync(string teamKey, string userKey, string[] tenantRoles) => throw new NotImplementedException();
        protected override Task SetTeamMemberScopeOverridesAsync(string teamKey, string userKey, string[] scopeOverrides) => throw new NotImplementedException();
        protected override Task SetTeamMemberNameAsync(string teamKey, string userKey, string name) => throw new NotImplementedException();
        protected override Task SetTeamConsentInternalAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel) => throw new NotImplementedException();
        protected override Task SetTeamCustomRolesInternalAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => throw new NotImplementedException();
    }
}
