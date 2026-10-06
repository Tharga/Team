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

public class UserIdentityClaimWiringTests
{
    private const string Sub = "pairwise-sub";
    private const string Oid = "tenant-oid";
    private const string TeamKey = "team-1";

    private const string ValidAzureAdConfig = """
        { "AzureAd": { "Authority": "https://test.ciamlogin.com/test", "ClientId": "c", "TenantId": "t", "CallbackPath": "/signin-oidc" } }
        """;

    private static ClaimsPrincipal EntraPrincipal()
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Sub),
            new Claim(DirectoryClaimTypes.ObjectIdentifier, Oid),
            new Claim(ClaimTypes.Email, "ada@example.com")
        ], "Cookies"));

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig));
        builder.Configuration.AddJsonStream(stream);
        builder.Services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider());
        return builder;
    }

    [Fact]
    public void AddThargaTeam_WithoutTheOption_RegistersTheDefault()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam();

        using var provider = builder.Services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<UserIdentityResolver>().IsConfigured);
    }

    [Fact]
    public void AddThargaTeam_WithTheOption_RegistersItsResolver()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o => o.UserIdentityClaimTypes = [DirectoryClaimTypes.ObjectId]);

        using var provider = builder.Services.BuildServiceProvider();

        Assert.Equal([DirectoryClaimTypes.ObjectId], provider.GetRequiredService<UserIdentityResolver>().ClaimTypes);
    }

    [Fact]
    public void AddThargaTeam_WithTheOption_ReachesTheUserService()
    {
        var builder = CreateBuilder();
        builder.AddThargaTeam(o =>
        {
            o.UserIdentityClaimTypes = [DirectoryClaimTypes.ObjectId];
            o.Blazor.RegisterTeamService<FakeTeamService, RecordingUserService>();
        });

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<RecordingUserService>().IdentityResolver.IsConfigured);
    }

    [Fact]
    public void GranularPath_RegistersTheDefault()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamBlazor();

        using var provider = services.BuildServiceProvider();

        Assert.Same(UserIdentityResolver.Default, provider.GetRequiredService<UserIdentityResolver>());
    }

    [Theory]
    [InlineData(false, Sub)]
    [InlineData(true, Oid)]
    public async Task ClaimsBuilder_LooksTheCallerUpByTheConfiguredClaim(bool configured, string expected)
    {
        using var provider = BuildProvider(configured, out _);
        using var scope = provider.CreateScope();
        var builder = scope.ServiceProvider.GetRequiredService<TeamMembershipClaimsBuilder>();

        await builder.BuildAsync(EntraPrincipal(), TeamKey);

        Assert.Equal([expected], scope.ServiceProvider.GetRequiredService<RecordingUserService>().LookedUp);
    }

    [Theory]
    [InlineData(false, Sub)]
    [InlineData(true, Oid)]
    public async Task ConcreteUserService_ResolvedDirectly_UsesTheConfiguredClaim(bool configured, string expected)
    {
        using var provider = BuildProvider(configured, out _);
        using var scope = provider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<RecordingUserService>();

        var user = await userService.GetCurrentUserAsync(EntraPrincipal());

        Assert.Equal(expected, user.Identity);
    }

    [Theory]
    [InlineData(false, Sub)]
    [InlineData(true, Oid)]
    public async Task Authorizer_SubjectIsTheConfiguredClaim(bool configured, string expected)
    {
        using var provider = BuildProvider(configured, out _);
        using var scope = provider.CreateScope();

        var subject = await scope.ServiceProvider.GetRequiredService<TeamAuthorizer>().GetSubjectAsync();

        Assert.Equal(expected, subject);
    }

    [Theory]
    [InlineData(false, Sub)]
    [InlineData(true, Oid)]
    public async Task FirstSignInAudit_RecordsTheConfiguredClaim(bool configured, string expected)
    {
        using var provider = BuildProvider(configured, out var recorder);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IUserService>().GetCurrentUserAsync(EntraPrincipal());

        var entry = Assert.Single(recorder.Entries, e => e.Action == AuthAuditEntries.UserCreatedAction);
        Assert.Equal(expected, entry.CallerUserIdentity);
        Assert.Equal(expected, entry.CallerIdentity);
    }

    private static ServiceProvider BuildProvider(bool configured, out RecordingAuditLogger recorder)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider());

        recorder = new RecordingAuditLogger();
        services.AddSingleton(Options.Create(new AuditOptions()));
        services.AddSingleton<IAuditLogger>(recorder);
        services.AddSingleton<CompositeAuditLogger>();

        if (configured) services.AddSingleton(new UserIdentityResolver([DirectoryClaimTypes.ObjectId]));

        services.AddThargaTeamBlazor(o => o.RegisterTeamService<FakeTeamService, RecordingUserService>());

        return services.BuildServiceProvider();
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public readonly List<AuditEntry> Entries = [];
        public void Log(AuditEntry entry) => Entries.Add(entry);
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class StubAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(EntraPrincipal()));
    }

    private sealed record TestUser : IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
    }

    private sealed class RecordingUserService(AuthenticationStateProvider asp) : UserServiceBase(asp, cache: new InMemoryTeamCache())
    {
        public List<string> LookedUp { get; } = [];

        protected override TimeSpan? LastSeenStampInterval => null;

        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal)
        {
            var identity = ResolveUserIdentity(claimsPrincipal);
            LookedUp.Add(identity);
            var user = new TestUser { Key = $"u-{identity}", Identity = identity, EMail = "ada@example.com" };
            RaiseUserCreated(user, claimsPrincipal);
            return Task.FromResult<IUser>(user);
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
