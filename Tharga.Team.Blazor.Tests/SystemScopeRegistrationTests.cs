using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The built-in system scopes must register exactly once, however many times a host wires the library up.
/// </summary>
/// <remarks>
/// <b>The guarding moved into the registry, and these tests are why it had to.</b>
/// <c>SystemScopeRegistry.Register</c> used to throw on a duplicate name while <c>AddThargaSystemScopes</c>
/// reused the registry already in the collection — so every registration site needed its own
/// <c>if (scopes.All.All(...))</c>, and a site that forgot one took the host's startup with it. It now skips
/// a name already present, and the per-site guards are gone.
/// <para>
/// It failed that way twice. <c>teams:purge</c> shipped unguarded in 3.13.0 — the registration sat under the
/// <c>teams:delete</c> guard's indentation without braces, so it ran unconditionally. Then 3.14.0 hit the
/// half a per-site guard can never cover: the library began registering <c>simulation:demo</c>, a name hosts
/// had been registering themselves, and <b>the unguarded call was in host code</b>
/// (<a href="https://github.com/Tharga/Team/issues/237">#237</a>). Nothing the library writes can reach
/// that, which is what made the per-site approach the wrong shape rather than merely error-prone.
/// </para>
/// <para>
/// <b>Team scopes are deliberately not the same.</b> <c>ScopeRegistry.Register</c> still throws, because a
/// team scope also carries an access level and a grant-only flag that two registrations can genuinely
/// disagree about. A system scope carries a name and catalogue text, and the name is the identity.
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

    /// <summary>
    /// The reverse order of the test above, and the one Tharga/Team#237 reports: the library registers
    /// first, then the host registers the same scope under its own name for it.
    /// </summary>
    /// <remarks>
    /// <b>Only the library's own registrations are guarded, and it cannot guard the host's.</b> Until 3.14.0
    /// no library code registered <c>simulation:demo</c>, so a host wanting demo mode had to register it
    /// itself. Commit <c>fa279ec</c> made the library register it too, which turns every such host's
    /// pre-existing line into a duplicate.
    /// </remarks>
    [Fact]
    public void AHostRegisteringAScopeTheLibraryAlreadyRegistered_DoesNotThrow()
    {
        var services = Arrange();

        var exception = Record.Exception(() => services.AddThargaSystemScopes(scopes =>
            scopes.Register(SimulationScopes.Demo, HostDescription)));

        Assert.Null(exception);
    }

    /// <summary>
    /// The descriptions differ, which is why keying idempotency on name <i>and</i> description would not fix
    /// the reported failure.
    /// </summary>
    /// <remarks>
    /// The issue proposes making re-registration idempotent "when key and description match, and throw only
    /// on a genuine conflict". The reporting host describes the scope as <see cref="HostDescription"/>; the
    /// library describes it as what it does to your claims. A match on both fields is never satisfied here,
    /// so that rule would leave the failure exactly as it is. This test exists so nobody re-derives that the
    /// expensive way.
    /// </remarks>
    [Fact]
    public void TheLibraryAndAHostDescribeTheSameScopeDifferently()
    {
        var services = Arrange();

        var registry = services.BuildServiceProvider().GetRequiredService<ISystemScopeRegistry>();
        var libraryDescription = registry.All.Single(s => s.Name == SimulationScopes.Demo).Description;

        Assert.NotEqual(HostDescription, libraryDescription);
    }

    /// <summary>
    /// A duplicate registration must leave one entry, not two — the scope catalogue renders a row per entry.
    /// </summary>
    [Fact]
    public void AScopeRegisteredTwice_AppearsOnceInTheCatalogue()
    {
        var services = Arrange();

        services.AddThargaSystemScopes(scopes => scopes.Register(SimulationScopes.Demo, HostDescription));

        var registry = services.BuildServiceProvider().GetRequiredService<ISystemScopeRegistry>();
        Assert.Single(registry.All, s => s.Name == SimulationScopes.Demo);
    }

    /// <summary>
    /// The consumer's reported shape: the same host built more than once in one process, as
    /// <c>WebApplicationFactory&lt;Program&gt;</c> does once per test.
    /// </summary>
    /// <remarks>
    /// <b>The host registers before the library here, deliberately.</b> That is the order under which the
    /// issue reports the first build succeeding, so if this fails on the second call the cause is repeated
    /// construction rather than registration order — and if it passes, the "first one works" detail is
    /// explained by ordering inside their host instead. The two are distinguishable only by running both.
    /// </remarks>
    [Fact]
    public void BuildingTheSameHostTwiceInOneProcess_DoesNotThrow()
    {
        static void BuildHost()
        {
            var services = new ServiceCollection();
            services.AddThargaSystemScopes(scopes => scopes.Register(SimulationScopes.Demo, HostDescription));
            services.AddThargaTeamBlazor(o => o.RegisterTeamService<FakeTeamService, FakeUserService>());
            services.BuildServiceProvider();
        }

        BuildHost();

        var exception = Record.Exception(BuildHost);

        Assert.Null(exception);
    }

    private const string HostDescription = "Use demo mode and view-as on the profile page";

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
