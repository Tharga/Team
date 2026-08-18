using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The built-in system scopes must register exactly once, however many times a host wires the library up.
/// </summary>
/// <remarks>
/// <b>Every registration in the block is individually guarded for a reason.</b>
/// <c>SystemScopeRegistry.Register</c> throws on a duplicate name, and <c>AddThargaSystemScopes</c> reuses
/// the registry already in the collection rather than making a new one — so an unguarded line throws on the
/// second pass and takes the host's startup with it.
/// <para>
/// <c>teams:purge</c> shipped that way in 3.13.0: the registration sat under the <c>teams:delete</c> guard's
/// indentation without braces, so it ran unconditionally. Nothing caught it, because no test registered
/// twice and no test asserted the scope existed at all. These do both.
/// </para>
/// <para>
/// Note the <c>RegisterTeamService</c> call in every arrangement — the scope block sits inside
/// <c>if (o._teamService != null)</c>, so a bare <c>AddThargaTeamBlazor()</c> never reaches it and a test
/// written without it passes while checking nothing.
/// </para>
/// </remarks>
public class SystemScopeRegistrationTests
{
    private static ServiceCollection Arrange()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamBlazor(o => o.RegisterTeamService<FakeTeamService, FakeUserService>());
        return services;
    }

    [Fact]
    public void AddThargaTeamBlazor_CalledTwice_DoesNotThrow()
    {
        var services = Arrange();

        var exception = Record.Exception(() =>
            services.AddThargaTeamBlazor(o => o.RegisterTeamService<FakeTeamService, FakeUserService>()));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(SystemTeamScopes.Delete)]
    [InlineData(SystemTeamScopes.Purge)]
    [InlineData(SystemTeamScopes.Manage)]
    [InlineData(SystemTeamScopes.SetOwner)]
    [InlineData(SystemUserScopes.Manage)]
    public void AddThargaTeamBlazor_RegistersBuiltInSystemScope(string scopeName)
    {
        var services = Arrange();

        var registry = services.BuildServiceProvider().GetRequiredService<ISystemScopeRegistry>();
        Assert.Single(registry.All, s => s.Name == scopeName);
    }

    [Fact]
    public void AddThargaTeamBlazor_AfterHostRegisteredTheSameScope_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddThargaSystemScopes(scopes => scopes.Register(SystemTeamScopes.Purge, "Host's own."));

        var exception = Record.Exception(() =>
            services.AddThargaTeamBlazor(o => o.RegisterTeamService<FakeTeamService, FakeUserService>()));

        Assert.Null(exception);
    }

    private sealed class FakeUserService(AuthenticationStateProvider asp) : UserServiceBase(asp)
    {
        protected override Task<IUser> GetUserAsync(ClaimsPrincipal claimsPrincipal) => Task.FromResult<IUser>(null);
        protected override async IAsyncEnumerable<IUser> GetAllAsync() { yield break; }
    }

    private sealed class FakeTeamService(IUserService userService) : TeamServiceBase(userService)
    {
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
