using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Mcp;
using Tharga.Team;

namespace Tharga.Team.Mcp.Tests;

/// <summary>
/// Naming a team on an MCP call.
/// </summary>
/// <remarks>
/// Everything on this surface used to derive from the caller's <c>TeamKey</c> claim, so a call could only
/// address the team the caller was anchored to — and a system key is anchored to none. That made the
/// consent rule unimplementable here: there was no team whose consented level could be resolved.
/// <para>
/// <b>The tests that matter are the narrowing ones.</b> Selection must never hand a caller a scope they
/// did not already have in the named team. The tempting bug is to answer a question about the selected
/// team using the claims the principal carries — which describe a <i>different</i> team.
/// </para>
/// </remarks>
public class McpTeamSelectionTests
{
    private const string Header = "X-Team-Key";
    private const string Anchored = "anchored-team";
    private const string Target = "target-team";

    private sealed record FakeUser(string Key, string Identity, string EMail) : IUser;

    private sealed record FakeMember(string Key, AccessLevel AccessLevel) : ITeamMember
    {
        public string Name => null;
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public MembershipState? State => MembershipState.Member;
        public string[] TenantRoles => [];
        public string[] ScopeOverrides => [];
    }

    private sealed record FakeTeam(string Key, AccessLevel? ConsentAccessLevel) : ITeam
    {
        public string Name => Key;
        public string Icon => null;
        public string[] ConsentedRoles => ["Support"];
    }

    private static async IAsyncEnumerable<ITeam> Teams(params ITeam[] teams)
    {
        foreach (var t in teams) yield return t;
        await Task.CompletedTask;
    }


    /// <summary>
    /// Puts the scoped team services on the request scope, which is where the accessor reads them.
    /// </summary>
    /// <remarks>
    /// It cannot take them in its constructor: it is registered as a singleton, and capturing a scoped
    /// service in one is what <c>ValidateOnBuild</c> refuses — it stopped the sample starting at all.
    /// Wiring them through <c>RequestServices</c> here exercises the same path the application uses.
    /// </remarks>
    private static void WithRequestServices(
        DefaultHttpContext httpContext, IUserService userService, ITeamService teamService, IScopeRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddSingleton(userService);
        services.AddSingleton(teamService);
        services.AddSingleton(registry);
        httpContext.RequestServices = services.BuildServiceProvider();
    }

    private static (HttpContextMcpContextAccessor Sut, ITeamService TeamService) Build(
        string selectedTeam,
        ITeamMember memberOfTarget = null,
        ITeam consentedTeam = null,
        string[] roles = null,
        bool systemKey = false,
        string[] scopesAtLevel = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
        if (!systemKey) claims.Add(new Claim(TeamClaimTypes.TeamKey, Anchored));
        if (systemKey) claims.Add(new Claim(TeamClaimTypes.IsSystemKey, "true"));
        foreach (var r in roles ?? []) claims.Add(new Claim(ClaimTypes.Role, r));

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        if (selectedTeam != null) httpContext.Request.Headers[Header] = selectedTeam;

        var httpAccessor = Substitute.For<IHttpContextAccessor>();
        httpAccessor.HttpContext.Returns(httpContext);

        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>())
            .Returns(new FakeUser("user-1", "user-1", "u@example.com"));

        var teamService = Substitute.For<ITeamService>();
        // Unconfigured NSubstitute calls return an auto-substitute, not null, so every other team would
        // otherwise resolve as a member -- and "unknown team" would be untestable.
        teamService.GetTeamMemberAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((ITeamMember)null);
        teamService.GetTeamMemberAsync(Target, "user-1").Returns(memberOfTarget);
        teamService.GetConsentedTeamsAsync(Arg.Any<string[]>())
            .Returns(_ => Teams(consentedTeam == null ? [] : [consentedTeam]));

        // A key is now resolved by the team's own consent rather than by matching its roles, so the
        // team-level read has to be stubbed too. That is the behaviour change: a system key is admitted
        // by the consented *level*, and previously could never be admitted at all, having no roles.
        teamService.GetTeamByKeyAsync(Arg.Any<string>()).Returns((ITeam)null);
        if (consentedTeam != null) teamService.GetTeamByKeyAsync(consentedTeam.Key).Returns(consentedTeam);

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(scopesAtLevel ?? ["team:read"]);

        WithRequestServices(httpContext, userService, teamService, registry);
        var sut = new HttpContextMcpContextAccessor(httpAccessor, Options.Create(new McpTeamOptions()));

        return (sut, teamService);
    }

    [Fact]
    public void NoHeader_BehavesExactlyAsBefore()
    {
        var (sut, _) = Build(selectedTeam: null);

        var ctx = sut.Current.AsTeamContext();

        Assert.Equal(Anchored, ctx.TeamId);
    }

    [Fact]
    public void AMemberOfTheNamedTeam_AddressesIt()
    {
        var (sut, _) = Build(Target, memberOfTarget: new FakeMember("user-1", AccessLevel.Administrator));

        var ctx = sut.Current.AsTeamContext();

        Assert.Equal(Target, ctx.TeamId);
    }

    /// <summary>
    /// The case that is impossible today: a system key is anchored to no team at all, so before this it
    /// could never address one.
    /// </summary>
    [Fact]
    public void ASystemKey_CanAddressATeamItConsentsTo()
    {
        var (sut, _) = Build(Target,
            consentedTeam: new FakeTeam(Target, AccessLevel.Viewer),
            roles: ["Support"],
            systemKey: true);

        var ctx = sut.Current.AsTeamContext();

        Assert.Equal(Target, ctx.TeamId);
        Assert.Equal(McpScope.System, ctx.Scope);
    }

    /// <summary>A non-member holding a consented global role reaches the team at the consented level.</summary>
    [Fact]
    public void AConsentedNonMember_AddressesTheTeam()
    {
        var (sut, _) = Build(Target,
            consentedTeam: new FakeTeam(Target, AccessLevel.Viewer),
            roles: ["Support"]);

        Assert.Equal(Target, sut.Current.AsTeamContext().TeamId);
    }

    [Fact]
    public void NeitherMemberNorConsented_IsRefused()
    {
        var (sut, _) = Build(Target);

        var ex = Assert.Throws<UnauthorizedAccessException>(() => sut.Current);
        Assert.Contains(Target, ex.Message);
    }

    /// <summary>
    /// Refused, not answered with an empty set. The caller named a specific team, so an empty answer
    /// would read as "that team has nothing in it" rather than "you cannot see it".
    /// </summary>
    [Fact]
    public void AnUnknownTeam_IsRefusedRatherThanEmpty()
    {
        var (sut, _) = Build("no-such-team");

        Assert.Throws<UnauthorizedAccessException>(() => sut.Current);
    }

    /// <summary>
    /// <b>The narrowing property.</b> The principal carries <c>team:manage</c> for the team it is
    /// anchored to. Selecting a different team where it is only a Viewer must not carry that over — the
    /// scopes are recomputed for the named team, never read from the claims.
    /// </summary>
    [Fact]
    public void Selecting_DoesNotCarryTheAnchoredTeamsScopesAcross()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-1"),
            new(TeamClaimTypes.TeamKey, Anchored),
            new(TeamClaimTypes.Scope, "team:manage"),
            new(TeamClaimTypes.Scope, "member:manage"),
        };
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        httpContext.Request.Headers[Header] = Target;

        var httpAccessor = Substitute.For<IHttpContextAccessor>();
        httpAccessor.HttpContext.Returns(httpContext);

        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>())
            .Returns(new FakeUser("user-1", "user-1", "u@example.com"));

        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamMemberAsync(Target, "user-1").Returns(new FakeMember("user-1", AccessLevel.Viewer));
        teamService.GetConsentedTeamsAsync(Arg.Any<string[]>()).Returns(_ => Teams());

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(AccessLevel.Viewer, Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(["team:read"]);

        WithRequestServices(httpContext, userService, teamService, registry);
        var accessor = new HttpContextMcpContextAccessor(httpAccessor, Options.Create(new McpTeamOptions()));

        var checker = new McpScopeChecker(httpAccessor, accessor);

        Assert.True(checker.Has("team:read"));      // what they hold in the named team
        Assert.False(checker.Has("team:manage"));   // what they hold in the *anchored* team
        Assert.False(checker.Has("member:manage"));
    }

    /// <summary>
    /// A suspended member cannot select the team either — suspension means no access, and a route that
    /// forgot it would become the way around being suspended.
    /// </summary>
    [Fact]
    public void ASuspendedMember_IsRefused()
    {
        var suspended = Substitute.For<ITeamMember>();
        suspended.Key.Returns("user-1");
        suspended.AccessLevel.Returns(AccessLevel.Administrator);
        suspended.SuspendedAt.Returns(new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc));

        var (sut, _) = Build(Target, memberOfTarget: suspended);

        Assert.Throws<UnauthorizedAccessException>(() => sut.Current);
    }

    /// <summary>A blank header is no selection, not a selection of nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankHeader_IsTreatedAsNoSelection(string value)
    {
        var (sut, _) = Build(selectedTeam: value);

        Assert.Equal(Anchored, sut.Current.AsTeamContext().TeamId);
    }

    /// <summary>Naming a team promotes a plain user to Team scope — otherwise the providers stay hidden.</summary>
    [Fact]
    public void SelectingPromotesAUserWithNoAnchoredTeamToTeamScope()
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        httpContext.Request.Headers[Header] = Target;

        var httpAccessor = Substitute.For<IHttpContextAccessor>();
        httpAccessor.HttpContext.Returns(httpContext);

        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>())
            .Returns(new FakeUser("user-1", "user-1", "u@example.com"));

        var teamService = Substitute.For<ITeamService>();
        teamService.GetTeamMemberAsync(Target, "user-1").Returns(new FakeMember("user-1", AccessLevel.User));
        teamService.GetConsentedTeamsAsync(Arg.Any<string[]>()).Returns(_ => Teams());

        var registry = Substitute.For<IScopeRegistry>();
        registry.GetEffectiveScopes(Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(["team:read"]);

        WithRequestServices(httpContext, userService, teamService, registry);
        var sut = new HttpContextMcpContextAccessor(httpAccessor, Options.Create(new McpTeamOptions()));

        Assert.Equal(McpScope.Team, sut.Current.Scope);
    }
}
