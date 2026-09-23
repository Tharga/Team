using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Regression guard for issue #95 ("SetTeamConsentAsync emits no audit entry"). Wires ITeamService the
/// way AddThargaTeamBlazor does (audit configured) and checks that a consent change actually reaches the
/// audit sink through the fully-resolved service — the behaviour #87 silently broke via registration
/// order. This asserts audit *fires end-to-end*, not the chain shape: `AuthorizationTeamServiceDecorator`
/// now sits outermost over the audit decorator (3.1.2), so the caller must be authorized to reach audit.
/// </summary>
public class ConsentAuditWiringTests
{
    private const string TeamKey = "team-1";

    [Fact]
    public async Task SetTeamConsentAsync_Through_Wired_Service_Emits_Audit_Entry()
    {
        var (sp, recorder) = BuildProvider();
        using var scope = sp.CreateScope();
        var teamService = scope.ServiceProvider.GetRequiredService<ITeamService>();

        await teamService.SetTeamConsentAsync(TeamKey, new[] { "Developer" }, AccessLevel.Viewer);

        Assert.Contains(recorder.Entries, e => e.Action == "set-consent" && e.TeamKey == TeamKey);
    }

    [Fact]
    public async Task SetTeamConsentAsync_Through_Wired_Service_Records_ConsentMetadata()
    {
        var (sp, recorder) = BuildProvider();
        using var scope = sp.CreateScope();
        var teamService = scope.ServiceProvider.GetRequiredService<ITeamService>();

        await teamService.SetTeamConsentAsync(TeamKey, new[] { "Developer" }, AccessLevel.Viewer);

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(nameof(AccessLevel.Viewer), entry.Metadata[AuditMetadataKeys.ConsentAccessLevelNew]);
        Assert.Equal("Developer", entry.Metadata[AuditMetadataKeys.ConsentRoles]);
    }

    private static (ServiceProvider sp, RecordingAuditLogger recorder) BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider());

        // Audit configured (as a host would via AddThargaAuditLogging).
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

    // Authorized for team-1: AuthorizationTeamServiceDecorator (outermost since 3.1.2) requires
    // team:manage bound to the acted-on team before the call reaches the audit decorator — and, for consent,
    // held as a member, which the member key says.
    private sealed class StubAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(TeamClaimTypes.TeamKey, TeamKey),
                new Claim(TeamClaimTypes.MemberKey, "member-1"),
                new Claim(TeamClaimTypes.Scope, TeamScopes.Manage)
            ], authenticationType: "Test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed class FakeUserService(AuthenticationStateProvider asp) : UserServiceBase(asp)
    {
        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal) => Task.FromResult<IUser>(null);
        protected override async IAsyncEnumerable<IUser> GetAllAsync() { yield break; }
    }

    private sealed class FakeTeamService(IUserService userService) : TeamServiceBase(userService)
    {
        // Only the consent path is exercised; everything else can be unimplemented.
        protected override Task SetTeamConsentInternalAsync(string teamKey, string[] consentedRoles, AccessLevel? accessLevel) => Task.CompletedTask;
        protected override async IAsyncEnumerable<ITeam> GetConsentedTeamsInternalAsync(string[] userRoles) { yield break; }
        protected override Task SetTeamCustomRolesInternalAsync(string teamKey, IReadOnlyList<TenantRoleDefinition> customRoles) => Task.CompletedTask;

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
    }
}
