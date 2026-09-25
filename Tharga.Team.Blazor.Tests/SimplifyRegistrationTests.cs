using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Tests;

public class SimplifyRegistrationTests
{
    [Fact]
    public void RegisterTeamService_ThreeParams_RegistersITeamManagementService()
    {
        var services = new ServiceCollection();

        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService, StubMember>();
        });

        Assert.Contains(services, d => d.ServiceType == typeof(ITeamManagementService));
    }

    /// <summary>
    /// The two-argument overload registers nothing injectable <b>only when no member type can be
    /// inferred</b> — as here, where <c>StubTeamService</c> derives straight from <c>TeamServiceBase</c>
    /// and so carries none.
    /// </summary>
    /// <remarks>
    /// <b>This test used to say something stronger, and it was wrong.</b> It asserted the two-argument
    /// overload never registers <c>ITeamManagementService</c>, which pinned a gap as intended behaviour
    /// and broke a consuming host's startup twice — at 3.5.2 and again at 3.10.0 — because any fix would
    /// have failed this test and read as a regression.
    /// <para>
    /// It now records the narrow truth that remains: with nothing to infer from, the facets cannot be
    /// registered. <c>TeamServiceCompletenessCheck</c> reports that at startup instead of leaving it to
    /// surface when a component renders.
    /// </para>
    /// </remarks>
    [Fact]
    public void RegisterTeamService_TwoParams_WithNoInferableMemberType_RegistersNoManagementService()
    {
        var services = new ServiceCollection();

        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService>();
        });

        Assert.Null(TeamMemberTypeResolver.Resolve(typeof(StubTeamService)));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ITeamManagementService));
    }

    [Fact]
    public void RegisterTeamService_AutoRegistersDefaultScopes()
    {
        var services = new ServiceCollection();

        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService>();
        });

        Assert.Contains(services, d => d.ServiceType == typeof(IScopeRegistry));
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IScopeRegistry>();
        Assert.Contains(registry.All, s => s.Name == TeamScopes.Read);
        Assert.Contains(registry.All, s => s.Name == TeamScopes.Manage);
        Assert.Contains(registry.All, s => s.Name == TeamScopes.MemberManage);
        Assert.Contains(registry.All, s => s.Name == ApiKeyScopes.Manage);
    }

    [Fact]
    public void RegisterTeamService_RegistersTeamsDeleteSystemScope()
    {
        var services = new ServiceCollection();

        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService>();
        });

        var provider = services.BuildServiceProvider();
        var systemRegistry = provider.GetRequiredService<ISystemScopeRegistry>();
        Assert.Contains(systemRegistry.All, s => s.Name == SystemTeamScopes.Delete);
    }

    [Fact]
    public void RegisterTeamService_DoesNotOverrideExistingScopeRegistry()
    {
        var services = new ServiceCollection();
        services.AddThargaScopes(scopes =>
        {
            scopes.Register("custom:scope", AccessLevel.Viewer);
        });

        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService>();
        });

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IScopeRegistry>();
        Assert.Contains(registry.All, s => s.Name == "custom:scope");
        // Should not have added defaults since IScopeRegistry was already registered
        Assert.DoesNotContain(registry.All, s => s.Name == TeamScopes.Read);
    }

    [Fact]
    public void WithoutTeamService_DoesNotRegisterScopes()
    {
        var services = new ServiceCollection();

        services.AddThargaTeamBlazor();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IScopeRegistry));
    }
}

internal class StubMember : ITeamMember
{
    public string Key { get; init; }
    public string Name { get; init; }
    public Invitation Invitation { get; init; }
    public DateTime? LastSeen { get; init; }
    public MembershipState? State { get; init; }
    public AccessLevel AccessLevel { get; init; }
    public string[] TenantRoles { get; init; }
    public string[] ScopeOverrides { get; init; }
}

internal class StubTeamService : TeamServiceBase
{
    public StubTeamService() : base(null) { }
    protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => throw new NotImplementedException();
    protected override Task<ITeam> GetTeamAsync(string teamKey) => throw new NotImplementedException();
    protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName) => throw new NotImplementedException();
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
    protected override Task<string> GetTeamKeyByInviteKeyInternalAsync(string inviteKey) => throw new NotImplementedException();
}

/// <summary>
/// <see cref="StubTeamService"/>'s shape without the invitation lookup — the host Tharga/Team#286 describes.
/// </summary>
internal class StubTeamServiceWithoutInviteLookup : TeamServiceBase
{
    public StubTeamServiceWithoutInviteLookup() : base(null) { }
    protected override IAsyncEnumerable<ITeam> GetTeamsAsync(IUser user) => throw new NotImplementedException();
    protected override Task<ITeam> GetTeamAsync(string teamKey) => throw new NotImplementedException();
    protected override Task<ITeam> CreateTeamAsync(string teamKey, string name, IUser user, string displayName) => throw new NotImplementedException();
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

internal class StubUserService : UserServiceBase
{
    public StubUserService() : base(null) { }
    protected override Task<IUser> GetUserAsync(System.Security.Claims.ClaimsPrincipal claimsPrincipal) => throw new NotImplementedException();
    protected override IAsyncEnumerable<IUser> GetAllAsync() => throw new NotImplementedException();
}
