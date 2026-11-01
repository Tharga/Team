using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The access level a caller holds is the level <b>in the team being acted on</b>: read from the identity issued for that
/// team, never from whichever identity on the principal comes first.
/// </summary>
/// <remarks>
/// <b>Today a principal carries one team's claims, so this cannot pick up another team's level.</b> These build the principal
/// a later change could produce — a second identity for another team — and pin that every reader of the claim agrees: the
/// access-level proxy, the owner check, the key management privilege, and the audit entries. It is the shape of the scope
/// check that was tightened in 4.6.1.
/// </remarks>
public class AccessLevelPerTeamTests
{
    private const string A = "team-a";
    private const string B = "team-b";

    private static ClaimsIdentity Identity(string team, string level, params string[] scopes)
    {
        var claims = new List<Claim>();
        if (team != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, team));
        if (level != null) claims.Add(new Claim(TeamClaimTypes.AccessLevel, level));
        claims.AddRange(scopes.Select(x => new Claim(TeamClaimTypes.Scope, x)));
        claims.Add(new Claim(ClaimTypes.Name, "sam"));

        return new ClaimsIdentity(claims, "Test");
    }

    private static ClaimsPrincipal Principal(params ClaimsIdentity[] identities) => new(identities);

    /// <summary>The caller's own team has no level; another team's identity says Owner.</summary>
    private static ClaimsPrincipal OwnerOfSomeoneElse() => Principal(Identity(A, null), Identity(B, "Owner"));

    // ---- The one helper ----

    [Fact]
    public void TheLevel_OnTheIdentityForTheTeam_IsRead()
    {
        Assert.Equal(AccessLevel.Owner, TeamScopePolicy.AccessLevelFor(Principal(Identity(A, "Owner")), A));
        Assert.Equal(AccessLevel.Viewer, TeamScopePolicy.AccessLevelFor(Principal(Identity(A, "Viewer")), A));
    }

    [Fact]
    public void ALevelOnAnotherTeamsIdentity_IsNotTheCallersLevelHere()
    {
        Assert.Null(TeamScopePolicy.AccessLevelFor(OwnerOfSomeoneElse(), A));
        Assert.Null(TeamScopePolicy.AccessLevelClaimFor(OwnerOfSomeoneElse(), A));
    }

    [Fact]
    public void WhenTheCallersTeamHasItsOwnLevel_AnotherTeamsHigherOneDoesNotRaiseIt()
    {
        var principal = Principal(Identity(A, "Viewer"), Identity(B, "Owner"));

        Assert.Equal(AccessLevel.Viewer, TeamScopePolicy.AccessLevelFor(principal, A));
    }

    [Fact]
    public void ForATeamThatIsNotTheCallers_ThereIsNoLevel_EvenWhenAnIdentityForItCarriesOne()
    {
        // The caller's team is the first TeamKey claim, as for scopes: a level for another team is not "acting as" it.
        Assert.Null(TeamScopePolicy.AccessLevelFor(Principal(Identity(A, null), Identity(B, "Owner")), B));
        Assert.Null(TeamScopePolicy.AccessLevelFor(Principal(Identity(A, "Owner")), B));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Nonsense")]
    [InlineData("99")]
    [InlineData("-1")]
    public void AMissingOrUnusableLevel_IsNone(string level)
    {
        Assert.Null(TeamScopePolicy.AccessLevelFor(Principal(Identity(A, level)), A));
    }

    [Fact]
    public void WithNoPrincipalOrNoTeam_ThereIsNoLevel()
    {
        Assert.Null(TeamScopePolicy.AccessLevelFor(null, A));
        Assert.Null(TeamScopePolicy.AccessLevelFor(Principal(Identity(A, "Owner")), null));
        Assert.Null(TeamScopePolicy.AccessLevelFor(Principal(Identity(A, "Owner")), ""));
        Assert.Null(TeamScopePolicy.AccessLevelFor(Principal(Identity(null, "Owner")), A));
    }

    [Fact]
    public void TheRawClaim_IsTheValueAsItWas_ForAnAuditEntry()
    {
        Assert.Equal("Administrator", TeamScopePolicy.AccessLevelClaimFor(Principal(Identity(A, "Administrator")), A));
        Assert.Equal("Nonsense", TeamScopePolicy.AccessLevelClaimFor(Principal(Identity(A, "Nonsense")), A));
    }

    // ---- The access-level proxy ----

    private static ITestService Proxy(ClaimsPrincipal principal, IAuditLogger audit = null)
    {
        var target = Substitute.For<ITestService>();
        target.ViewerMethod().Returns("viewer-ok");
        target.AdminMethod().Returns("admin-ok");

        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));

        return AccessLevelProxy<ITestService>.Create(target, accessor, audit);
    }

    [Fact]
    public void TheProxy_DoesNotLetAnotherTeamsOwnerLevelCount()
    {
        var proxy = Proxy(OwnerOfSomeoneElse());

        Assert.Throws<UnauthorizedAccessException>(() => proxy.ViewerMethod());
        Assert.Throws<UnauthorizedAccessException>(() => proxy.AdminMethod());
    }

    [Fact]
    public void TheProxy_StillAdmitsTheCallerByTheirOwnLevel_WhateverElseThePrincipalCarries()
    {
        var proxy = Proxy(Principal(Identity(A, "Administrator"), Identity(B, "Viewer")));

        Assert.Equal("admin-ok", proxy.AdminMethod());
    }

    [Fact]
    public void TheProxy_DoesNotLetAHigherLevelOnAnotherIdentityLiftTheCallersOwn()
    {
        var proxy = Proxy(Principal(Identity(A, "Viewer"), Identity(B, "Owner")));

        Assert.Equal("viewer-ok", proxy.ViewerMethod());
        Assert.Throws<UnauthorizedAccessException>(() => proxy.AdminMethod());
    }

    [Fact]
    public void TheProxysAuditEntry_RecordsTheLevelInTheTeam_NotAnotherTeamsLevel()
    {
        var audit = Substitute.For<IAuditLogger>();
        var entries = new List<AuditEntry>();
        audit.When(x => x.Log(Arg.Any<AuditEntry>())).Do(x => entries.Add(x.Arg<AuditEntry>()));

        Assert.Throws<UnauthorizedAccessException>(() => Proxy(OwnerOfSomeoneElse(), audit).ViewerMethod());
        Proxy(Principal(Identity(A, "Viewer"), Identity(B, "Owner")), audit).ViewerMethod();

        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].AccessLevel);
        Assert.Equal(A, entries[0].TeamKey);
        Assert.Equal("Viewer", entries[1].AccessLevel);
    }

    // ---- The owner check ----

    private static TeamAuthorizer Authorizer(ClaimsPrincipal principal)
    {
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));

        return new TeamAuthorizer(accessor);
    }

    [Fact]
    public async Task TheOwnerOfAnotherTeam_IsNotTheOwnerHere()
    {
        Assert.False(await Authorizer(OwnerOfSomeoneElse()).IsOwnerOfAsync(A));
        Assert.False(await Authorizer(Principal(Identity(A, "Viewer"), Identity(B, "Owner"))).IsOwnerOfAsync(A));
    }

    [Fact]
    public async Task TheOwnerOfTheTeam_IsStillTheOwner()
    {
        Assert.True(await Authorizer(Principal(Identity(A, "Owner"))).IsOwnerOfAsync(A));
        Assert.True(await Authorizer(Principal(Identity(A, "Owner"), Identity(B, "Viewer"))).IsOwnerOfAsync(A));
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Nonsense")]
    [InlineData(null)]
    public async Task AnythingButOwner_IsNotTheOwner(string level)
    {
        Assert.False(await Authorizer(Principal(Identity(A, level))).IsOwnerOfAsync(A));
    }

    // ---- Key management ----

    private static async Task<string[]> PrivateKeysSeen(ClaimsPrincipal principal)
    {
        var theirs = Substitute.For<IApiKey>();
        theirs.Key.Returns("theirs");
        theirs.OwnerMemberKey.Returns("someone-else");

        var inner = Substitute.For<IApiKeyAdministrationService>();
        inner.GetKeysAsync(A).Returns(_ => One(theirs));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = principal });

        var seen = new List<string>();
        await foreach (var key in new ApiKeyManagementService(inner, accessor).GetKeysAsync(A, PrivateKeyScope.All, allowPrivileged: true)) seen.Add(key.Key);

        return [.. seen];
    }

    private static async IAsyncEnumerable<IApiKey> One(IApiKey key)
    {
        yield return key;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AnotherTeamsAdministratorLevel_DoesNotOpenThisTeamsPrivateKeys()
    {
        var principal = Principal(Identity(A, null), Identity(B, "Administrator"));

        Assert.Empty(await PrivateKeysSeen(principal));
    }

    [Fact]
    public async Task TheTeamsOwnAdministrator_StillSeesThem_WhenTheHostAllowsIt()
    {
        Assert.Equal(["theirs"], await PrivateKeysSeen(Principal(Identity(A, "Administrator"), Identity(B, "Viewer"))));
    }

    // ---- The audit entries ----

    private static IHttpContextAccessor HttpAs(ClaimsPrincipal principal)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = principal });

        return accessor;
    }

    [Fact]
    public void ACreatedAuditEntry_RecordsTheLevelInItsTeam_NotAnotherTeamsLevel()
    {
        var factory = new AuditEntryFactory(HttpAs(OwnerOfSomeoneElse()));

        var entry = factory.Create("feature", "action");

        Assert.Equal(A, entry.TeamKey);
        Assert.Null(entry.AccessLevel);
    }

    [Fact]
    public void ACreatedAuditEntry_ForATeamTheCallerIsNotActingAs_RecordsNoLevel()
    {
        var factory = new AuditEntryFactory(HttpAs(Principal(Identity(A, "Viewer"), Identity(B, "Owner"))));

        var entry = factory.Create("feature", "action", teamKey: B);

        Assert.Equal(B, entry.TeamKey);
        Assert.Null(entry.AccessLevel);
    }

    [Fact]
    public void ACreatedAuditEntry_RecordsTheCallersOwnLevel()
    {
        var factory = new AuditEntryFactory(HttpAs(Principal(Identity(A, "Viewer"), Identity(B, "Owner"))));

        Assert.Equal("Viewer", factory.Create("feature", "action").AccessLevel);
    }

    [Fact]
    public void AScopeProxysAuditEntry_RecordsTheLevelInTheTeam()
    {
        var audit = Substitute.For<IAuditLogger>();
        var entries = new List<AuditEntry>();
        audit.When(x => x.Log(Arg.Any<AuditEntry>())).Do(x => entries.Add(x.Arg<AuditEntry>()));

        var target = Substitute.For<IScopedTestService>();
        target.ReadMethod(Arg.Any<string>()).Returns("read-ok");
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(Principal(Identity(A, null, "doc:read"), Identity(B, "Owner"))));
        var proxy = ScopeProxy<IScopedTestService>.Create(target, accessor, ServiceScopeKind.Team, audit);

        proxy.ReadMethod(A);

        var entry = Assert.Single(entries);
        Assert.Equal(A, entry.TeamKey);
        Assert.Null(entry.AccessLevel);
    }
}
