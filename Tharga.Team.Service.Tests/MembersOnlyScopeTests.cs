using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A scope the access level grants to members only is not given to an API key by the level (Tharga/Team#412).
/// </summary>
/// <remarks>
/// <b>The cases are the ones a host depends on:</b> a member at the level holds it, a key at the same level does not,
/// and a key still holds it when a role or an override names it. Every place that composes a key's scopes is asserted,
/// because one that was missed would hand the scope to every application key while the others looked fine.
/// </remarks>
public class MembersOnlyScopeTests
{
    private const string TeamKey = "acme";
    private const string Secret = "the-secret";
    private const string Read = "data:read";
    private const string Write = "data:write";
    private const string Members = "data:members";
    private const string AdminMembers = "data:admin-members";

    private static ScopeRegistry Registry()
    {
        var r = new ScopeRegistry();
        r.Register(Read, AccessLevel.Viewer);
        r.Register(Write, AccessLevel.User);
        r.Register(Members, AccessLevel.User, "Members only", ScopePrincipals.Members);
        r.Register(AdminMembers, AccessLevel.Administrator, "Members only", null, ScopePrincipals.Members);
        return r;
    }

    // ---- The registry ----

    [Fact]
    public void ADefaultScope_AppliesToEveryone()
    {
        var definition = new ScopeDefinition("a:b", AccessLevel.User);

        Assert.Equal(ScopePrincipals.All, definition.AppliesTo);
    }

    [Theory]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Administrator)]
    [InlineData(AccessLevel.Owner)]
    public void AMember_HoldsAMembersOnlyScope_AtTheLevel(AccessLevel level)
    {
        var scopes = Registry().GetEffectiveScopes(level, null, null);

        Assert.Contains(Members, scopes);
    }

    [Theory]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Administrator)]
    [InlineData(AccessLevel.Owner)]
    public void AKey_DoesNotHold_AMembersOnlyScope_FromTheLevel(AccessLevel level)
    {
        var scopes = Registry().GetEffectiveScopes(level, null, null, PrincipalKind.ApiKey);

        Assert.DoesNotContain(Members, scopes);
        Assert.DoesNotContain(AdminMembers, scopes);
    }

    [Fact]
    public void AKey_StillHoldsEveryOtherScope_OfTheLevel()
    {
        var scopes = Registry().GetEffectiveScopes(AccessLevel.User, null, null, PrincipalKind.ApiKey);

        Assert.Equal([Read, Write], scopes.Order().ToArray());
    }

    [Fact]
    public void AKey_HoldsAMembersOnlyScope_ThroughAnOverride()
    {
        var scopes = Registry().GetEffectiveScopes(AccessLevel.User, null, [Members], PrincipalKind.ApiKey);

        Assert.Contains(Members, scopes);
    }

    [Fact]
    public void AKey_HoldsAMembersOnlyScope_ThroughARole()
    {
        var registry = Registry();
        var roles = Substitute.For<ITenantRoleRegistry>();
        roles.GetScopesForRoles(Arg.Any<IEnumerable<string>>()).Returns([Members]);
        registry.SetRoleRegistry(roles);

        var scopes = registry.GetEffectiveScopes(AccessLevel.User, ["Operator"], null, PrincipalKind.ApiKey);

        Assert.Contains(Members, scopes);
    }

    [Fact]
    public void TheLevelAlone_ForAKey_OmitsIt()
    {
        var scopes = Registry().GetScopesForAccessLevel(AccessLevel.Administrator, PrincipalKind.ApiKey);

        Assert.DoesNotContain(Members, scopes);
        Assert.Contains(Read, scopes);
    }

    [Fact]
    public void WithoutAKind_TheLevelIsUnchanged_ForExistingCallers()
    {
        var registry = Registry();

        Assert.Contains(Members, registry.GetScopesForAccessLevel(AccessLevel.User));
        Assert.Contains(Members, registry.GetEffectiveScopes(AccessLevel.User, null));
    }

    // ---- The team's own role service ----

    [Fact]
    public async Task TheRoleService_FiltersForAKey_AndNotForAMember()
    {
        var team = Substitute.For<ITeamService>();
        team.GetTeamCustomRolesAsync(TeamKey).Returns([]);
        var sut = new TenantRoleService(team, Registry());

        var key = await sut.GetEffectiveScopesAsync(TeamKey, AccessLevel.User, [], [], PrincipalKind.ApiKey);
        var member = await sut.GetEffectiveScopesAsync(TeamKey, AccessLevel.User, [], []);

        Assert.DoesNotContain(Members, key);
        Assert.Contains(Members, member);
    }

    // ---- The key's own claims ----

    private static async Task<string[]> AuthenticatedScopes(AccessLevel level, string[] overrides = null)
    {
        var key = Substitute.For<IApiKey>();
        key.Key.Returns("key-id");
        key.TeamKey.Returns(TeamKey);
        key.Name.Returns("App key");
        key.Tags.Returns([]);
        key.AccessLevel.Returns(level);
        key.ScopeOverrides.Returns(overrides);
        var service = Substitute.For<IApiKeyAdministrationService>();
        service.GetByApiKeyAsync(Secret).Returns(key);
        var teams = Substitute.For<ITeamService>();
        teams.GetTeamCustomRolesAsync(TeamKey).Returns([]);

        var context = new DefaultHttpContext();
        context.Request.Headers[ApiKeyConstants.HeaderName] = Secret;
        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(ApiKeyConstants.SchemeName).Returns(new AuthenticationSchemeOptions());
        var loggers = Substitute.For<ILoggerFactory>();
        loggers.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        var handler = new ApiKeyAuthenticationHandler(options, loggers, UrlEncoder.Default, service, Registry(), tenantRoleService: new TenantRoleService(teams, Registry()));
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyConstants.SchemeName, "API Key", typeof(ApiKeyAuthenticationHandler)), context);
        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        return result.Principal.FindAll(TeamClaimTypes.Scope).Select(x => x.Value).Order().ToArray();
    }

    [Fact]
    public async Task AKeyAtTheLevel_IsIssuedEveryScopeButTheMembersOnlyOne()
    {
        Assert.Equal([Read, Write], await AuthenticatedScopes(AccessLevel.User));
    }

    /// <summary>The case in the issue: the application key kept at Administrator so it can write back.</summary>
    [Fact]
    public async Task AKeyAtAdministrator_IsNotIssuedAMembersOnlyScope()
    {
        var scopes = await AuthenticatedScopes(AccessLevel.Administrator);

        Assert.DoesNotContain(Members, scopes);
        Assert.DoesNotContain(AdminMembers, scopes);
    }

    [Fact]
    public async Task AKey_WithTheOverride_IsIssuedTheMembersOnlyScope()
    {
        Assert.Contains(Members, await AuthenticatedScopes(AccessLevel.User, [Members]));
    }

    // ---- The ceiling on granting a key ----

    private static ClaimsPrincipal Caller(AccessLevel level, params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new(TeamClaimTypes.TeamKey, TeamKey),
            new(TeamClaimTypes.AccessLevel, level.ToString()),
            new(TeamClaimTypes.MemberKey, "caller"),
        };
        claims.AddRange(scopes.Select(s => new Claim(TeamClaimTypes.Scope, s)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>
    /// A granter without the members-only scope can mint a key at the level, because the key would not carry it; the
    /// same level for a member would, so it is refused there. The ceiling compares what is actually granted.
    /// </summary>
    [Fact]
    public async Task TheCeiling_ComparesWhatAKeyWouldCarry_NotWhatAMemberWould()
    {
        var caller = Caller(AccessLevel.User, Read, Write);

        await new GrantCeiling(Registry(), null, PrincipalKind.ApiKey).EnsureCanGrantAsync(caller, TeamKey, AccessLevel.User, [], [], oversightExempts: true);

        var refused = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => new GrantCeiling(Registry(), null, PrincipalKind.Member).EnsureCanGrantAsync(caller, TeamKey, AccessLevel.User, [], [], oversightExempts: false));
        Assert.Contains(Members, refused.Message);
    }

    [Fact]
    public async Task TheCeiling_StillRefusesAKeyOverride_OfAMembersOnlyScopeTheCallerLacks()
    {
        var caller = Caller(AccessLevel.User, Read, Write);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => new GrantCeiling(Registry(), null, PrincipalKind.ApiKey).EnsureCanGrantAsync(caller, TeamKey, AccessLevel.User, [], [Members], oversightExempts: true));
    }
}
