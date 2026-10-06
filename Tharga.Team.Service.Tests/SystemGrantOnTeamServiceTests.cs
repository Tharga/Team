using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

public interface ISystemGrantTeamService
{
    [RequireScope(SystemGrantOnTeamServiceTests.GrantableScope, AllowSystemGrant = true)]
    string Update(string teamKey);

    [RequireScope(SystemGrantOnTeamServiceTests.GrantableScope)]
    string UpdateWithoutOptIn(string teamKey);
}

public interface ISystemServiceWithSystemGrant
{
    [RequireScope(SystemGrantOnTeamServiceTests.GrantableScope, AllowSystemGrant = true)]
    string Update();
}

/// <summary>
/// A system-granted operator acting on one named team through a team service, opted into per method with
/// <see cref="RequireScopeAttribute.AllowSystemGrant"/>.
/// </summary>
public class SystemGrantOnTeamServiceTests
{
    public const string GrantableScope = "features:manage";
    private const string TargetTeam = "team-a";

    private sealed class Implementation : ISystemGrantTeamService
    {
        public TeamAccessContext SeenAccess { get; private set; }

        public string Update(string teamKey)
        {
            SeenAccess = TeamAccess.Current;
            return "update-ok";
        }

        public string UpdateWithoutOptIn(string teamKey) => "update-ok";
    }

    private sealed class SystemImplementation : ISystemServiceWithSystemGrant
    {
        public string Update() => "update-ok";
    }

    private static ClaimsPrincipal Principal(string selectedTeam, string[] teamScopes, string[] systemScopes, string accessLevel = null)
    {
        var claims = new List<Claim>();
        if (selectedTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, selectedTeam));
        if (accessLevel != null) claims.Add(new Claim(TeamClaimTypes.AccessLevel, accessLevel));
        claims.AddRange(teamScopes.Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange(systemScopes.Select(s => new Claim(TeamClaimTypes.SystemScope, s)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal SystemGranted(string selectedTeam, string scope = GrantableScope)
        => Principal(selectedTeam, [], [scope]);

    private static IHttpContextAccessor Accessor(ClaimsPrincipal principal)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = principal });
        return accessor;
    }

    private static (ISystemGrantTeamService Proxy, Implementation Target, FakeAuditBackend Audit) Proxy(ClaimsPrincipal principal)
    {
        var target = new Implementation();
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        var proxy = ScopeProxy<ISystemGrantTeamService>.Create(target, Accessor(principal), ServiceScopeKind.Team, logger, AuditMode.Access);
        return (proxy, target, backend);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("team-b")]
    [InlineData(TargetTeam)]
    public void OptedIn_ASystemGrantedCaller_ActsOnTheNamedTeam_WhateverTeamIsSelected(string selectedTeam)
    {
        var (proxy, _, _) = Proxy(SystemGranted(selectedTeam));

        Assert.Equal("update-ok", proxy.Update(TargetTeam));
    }

    [Fact]
    public void OptedIn_AMemberHoldingTheTeamScope_StillPasses()
    {
        var (proxy, _, _) = Proxy(Principal(TargetTeam, [GrantableScope], []));

        Assert.Equal("update-ok", proxy.Update(TargetTeam));
    }

    [Fact]
    public void OptedIn_ATeamAdministratorWithoutTheScopeOrTheGrant_IsRefused()
    {
        var (proxy, _, _) = Proxy(Principal(TargetTeam, [TeamScopes.Manage], [], nameof(AccessLevel.Administrator)));

        Assert.Throws<UnauthorizedAccessException>(() => proxy.Update(TargetTeam));
    }

    [Fact]
    public void OptedIn_ASystemGrantOfADifferentScope_IsRefused()
    {
        var (proxy, _, _) = Proxy(SystemGranted(null, "audit:read"));

        Assert.Throws<UnauthorizedAccessException>(() => proxy.Update(TargetTeam));
    }

    [Fact]
    public void OptedIn_TheTeamScopeHeldForAnotherTeam_IsStillRefused()
    {
        var (proxy, _, _) = Proxy(Principal("team-b", [GrantableScope], []));

        Assert.Throws<UnauthorizedAccessException>(() => proxy.Update(TargetTeam));
    }

    [Fact]
    public void OptedIn_TheSystemGrantDoesNotExcuseACallNamingNoTeam()
    {
        var (proxy, _, _) = Proxy(SystemGranted(null));

        Assert.Throws<UnauthorizedAccessException>(() => proxy.Update(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("team-b")]
    [InlineData(TargetTeam)]
    public void NotOptedIn_ASystemGrantedCaller_IsRefusedAsBefore(string selectedTeam)
    {
        var (proxy, _, _) = Proxy(SystemGranted(selectedTeam));

        Assert.Throws<UnauthorizedAccessException>(() => proxy.UpdateWithoutOptIn(TargetTeam));
    }

    [Fact]
    public void AdmittedBySystemGrant_TheAccessDecisionIsForTheNamedTeam_AndSaysWhy()
    {
        var (proxy, target, _) = Proxy(SystemGranted("team-b"));

        proxy.Update(TargetTeam);

        Assert.Equal(TeamAccessKind.Team, target.SeenAccess.Kind);
        Assert.Equal(TargetTeam, target.SeenAccess.TeamKey);
        Assert.Contains(GrantableScope, target.SeenAccess.Reason);
    }

    [Fact]
    public void AdmittedByTheTeamScope_TheAccessDecisionCarriesNoSystemGrantReason()
    {
        var (proxy, target, _) = Proxy(Principal(TargetTeam, [GrantableScope], [GrantableScope]));

        proxy.Update(TargetTeam);

        Assert.Equal(TargetTeam, target.SeenAccess.TeamKey);
        Assert.Null(target.SeenAccess.Reason);
    }

    [Fact]
    public void AdmittedBySystemGrant_TheAuditEntryRecordsThePathAndTheTeamActedOn()
    {
        var (proxy, _, audit) = Proxy(SystemGranted("team-b"));

        proxy.Update(TargetTeam);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditScopeResult.Allowed, entry.ScopeResult);
        Assert.Equal(TargetTeam, entry.TeamKey);
        Assert.Equal(AuditMetadataKeys.AuthorizedViaSystemGrant, entry.Metadata[AuditMetadataKeys.AuthorizedVia]);
    }

    [Fact]
    public void AdmittedByTheTeamScope_TheAuditEntryRecordsNoSystemGrant()
    {
        var (proxy, _, audit) = Proxy(Principal(TargetTeam, [GrantableScope], []));

        proxy.Update(TargetTeam);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(TargetTeam, entry.TeamKey);
        Assert.True(entry.Metadata == null || !entry.Metadata.ContainsKey(AuditMetadataKeys.AuthorizedVia));
    }

    [Fact]
    public void AddTeamService_HonoursTheOptIn_ThroughTheContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Accessor(SystemGranted(null)));
        services.AddTeamService<ISystemGrantTeamService, Implementation>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ISystemGrantTeamService>();

        Assert.Equal("update-ok", service.Update(TargetTeam));
        Assert.Throws<UnauthorizedAccessException>(() => service.UpdateWithoutOptIn(TargetTeam));
    }

    [Fact]
    public void AddSystemService_RejectsTheOptIn_BecauseASystemServiceIsAlreadyAuthorizedByTheGrant()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddSystemService<ISystemServiceWithSystemGrant, SystemImplementation>());

        Assert.Contains(nameof(ISystemServiceWithSystemGrant.Update), exception.Message);
        Assert.Contains(nameof(RequireScopeAttribute.AllowSystemGrant), exception.Message);
    }
}
