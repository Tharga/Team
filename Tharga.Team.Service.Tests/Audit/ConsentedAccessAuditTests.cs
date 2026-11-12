using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

public interface IConsentedScopeService
{
    [RequireScope(ConsentedAccessAuditTests.Scope)]
    string Read(string teamKey);

    [RequireScope(ConsentedAccessAuditTests.Scope, Audit = AuditMode.Change)]
    string Change(string teamKey);
}

public interface IConsentedLevelService
{
    [RequireAccessLevel(AccessLevel.Viewer)]
    string Read();

    [RequireAccessLevel(AccessLevel.Viewer, Audit = AuditMode.Change)]
    string Change();
}

/// <summary>
/// A caller who reaches a team through its consent rather than a membership: every entry written for them says so,
/// and a host can stop recording their access traces while still recording what they changed and what they were refused.
/// </summary>
public class ConsentedAccessAuditTests
{
    public const string Scope = "case:read";
    private const string Team = "team-1";

    private sealed class ScopeImplementation : IConsentedScopeService
    {
        public string Read(string teamKey) => "read";
        public string Change(string teamKey) => "changed";
    }

    private sealed class LevelImplementation : IConsentedLevelService
    {
        public string Read() => "read";
        public string Change() => "changed";
    }

    private static ClaimsPrincipal Caller(string teamKey = Team, bool member = false, bool teamApiKey = false, bool systemKey = false, string[] scopes = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "caller") };
        if (teamKey != null)
        {
            claims.Add(new Claim(TeamClaimTypes.TeamKey, teamKey));
            claims.Add(new Claim(TeamClaimTypes.AccessLevel, nameof(AccessLevel.Administrator)));
        }

        claims.AddRange((scopes ?? [Scope]).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        if (member) claims.Add(new Claim(TeamClaimTypes.MemberKey, "member-1"));
        if (teamApiKey || systemKey) claims.Add(new Claim(TeamClaimTypes.ApiKeyId, "key-1"));
        if (systemKey) claims.Add(new Claim(TeamClaimTypes.IsSystemKey, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal Consented(string[] scopes = null) => Caller(scopes: scopes);

    private static ClaimsPrincipal Member() => Caller(member: true);

    private static IHttpContextAccessor Accessor(ClaimsPrincipal principal)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = principal });
        return accessor;
    }

    private static (IConsentedScopeService Proxy, FakeAuditBackend Audit) ScopeProxy(ClaimsPrincipal principal, bool recordConsentedAccess = true)
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create(new AuditOptions { RecordConsentedAccess = recordConsentedAccess });
        return (ScopeProxy<IConsentedScopeService>.Create(new ScopeImplementation(), Accessor(principal), ServiceScopeKind.Team, logger, AuditMode.Access), backend);
    }

    private static (IConsentedLevelService Proxy, FakeAuditBackend Audit) LevelProxy(ClaimsPrincipal principal, bool recordConsentedAccess = true)
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create(new AuditOptions { RecordConsentedAccess = recordConsentedAccess });
        return (AccessLevelProxy<IConsentedLevelService>.Create(new LevelImplementation(), Accessor(principal), logger, AuditMode.Access), backend);
    }

    private static string AuthorizedVia(AuditEntry entry)
        => entry.Metadata != null && entry.Metadata.TryGetValue(AuditMetadataKeys.AuthorizedVia, out var via) ? via : null;

    private static AuditEntry HostEntry(AuditEventType eventType, string authorizedVia) => new()
    {
        Timestamp = DateTime.UtcNow,
        EventType = eventType,
        Feature = "case",
        Action = "CaseRead",
        TeamKey = Team,
        Metadata = authorizedVia == null ? null : new Dictionary<string, string> { [AuditMetadataKeys.AuthorizedVia] = authorizedVia }
    };

    [Fact]
    public void AccessLevelProxy_AConsentedCallersChange_IsStillWritten_AsAStampedDataChange()
    {
        var (proxy, audit) = LevelProxy(Consented(), recordConsentedAccess: false);

        proxy.Change();

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditEventType.DataChange, entry.EventType);
        Assert.Equal(AuditMetadataKeys.AuthorizedViaConsent, AuthorizedVia(entry));
    }

    [Fact]
    public void AccessLevelProxy_AConsentedCallersRead_IsStamped()
    {
        var (proxy, audit) = LevelProxy(Consented());

        proxy.Read();

        Assert.Equal(AuditMetadataKeys.AuthorizedViaConsent, AuthorizedVia(Assert.Single(audit.Entries)));
    }

    [Fact]
    public void AccessLevelProxy_WithConsentedAccessOff_AConsentedCallersRead_IsNotWritten()
    {
        var (proxy, audit) = LevelProxy(Consented(), recordConsentedAccess: false);

        proxy.Read();

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public void AccessLevelProxy_WithConsentedAccessOff_AMembersRead_IsStillWritten_Unstamped()
    {
        var (proxy, audit) = LevelProxy(Member(), recordConsentedAccess: false);

        proxy.Read();

        Assert.Null(AuthorizedVia(Assert.Single(audit.Entries)));
    }

    [Fact]
    public void AuditProvenance_AgreesWithThePolicy()
    {
        Assert.True(AuditProvenance.IsConsented(Consented(), Team));
        Assert.False(AuditProvenance.IsConsented(Member(), Team));
        Assert.False(AuditProvenance.IsConsented(null, Team));
    }

    [Fact]
    public void CompositeLogger_WithConsentedAccessOff_DropsAStampedServiceCall_WhoeverWroteIt()
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create(new AuditOptions { RecordConsentedAccess = false });

        logger.Log(HostEntry(AuditEventType.ServiceCall, AuditMetadataKeys.AuthorizedViaConsent));

        Assert.Empty(backend.Entries);
    }

    [Theory]
    [InlineData(AuditEventType.DataChange, AuditMetadataKeys.AuthorizedViaConsent)]
    [InlineData(AuditEventType.ScopeDenial, AuditMetadataKeys.AuthorizedViaConsent)]
    [InlineData(AuditEventType.ServiceCall, AuditMetadataKeys.AuthorizedViaSystemGrant)]
    [InlineData(AuditEventType.ServiceCall, null)]
    public void CompositeLogger_WithConsentedAccessOff_KeepsEverythingButAConsentedAccessTrace(AuditEventType eventType, string authorizedVia)
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create(new AuditOptions { RecordConsentedAccess = false });

        logger.Log(HostEntry(eventType, authorizedVia));

        Assert.Single(backend.Entries);
    }

    [Fact]
    public void CompositeLogger_ByDefault_KeepsAConsentedAccessTrace()
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create();

        logger.Log(HostEntry(AuditEventType.ServiceCall, AuditMetadataKeys.AuthorizedViaConsent));

        Assert.Single(backend.Entries);
    }

    [Fact]
    public void HasConsentedAccess_ACallerInAnotherTeam_IsNot()
        => Assert.False(TeamScopePolicy.HasConsentedAccess(Caller("team-2"), Team));

    [Fact]
    public void HasConsentedAccess_ACallerInNoTeam_IsNot()
        => Assert.False(TeamScopePolicy.HasConsentedAccess(Caller(teamKey: null), Team));

    [Fact]
    public void HasConsentedAccess_AConsentedCaller_Is()
        => Assert.True(TeamScopePolicy.HasConsentedAccess(Consented(), Team));

    [Fact]
    public void HasConsentedAccess_AMember_IsNot()
        => Assert.False(TeamScopePolicy.HasConsentedAccess(Member(), Team));

    [Fact]
    public void HasConsentedAccess_ASystemKeyThroughConsent_Is()
        => Assert.True(TeamScopePolicy.HasConsentedAccess(Caller(systemKey: true), Team));

    [Fact]
    public void HasConsentedAccess_ATeamKey_IsNot()
        => Assert.False(TeamScopePolicy.HasConsentedAccess(Caller(teamApiKey: true), Team));

    [Fact]
    public void HasConsentedAccess_NamingNoTeam_IsNot()
        => Assert.False(TeamScopePolicy.HasConsentedAccess(Consented(), null));

    [Fact]
    public void RecordConsentedAccess_IsOnByDefault_SoNoExistingHostChanges()
        => Assert.True(new AuditOptions().RecordConsentedAccess);

    [Fact]
    public void ScopeProxy_AConsentedCallersChange_IsWrittenAsAStampedDataChange()
    {
        var (proxy, audit) = ScopeProxy(Consented());

        proxy.Change(Team);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditEventType.DataChange, entry.EventType);
        Assert.Equal(AuditMetadataKeys.AuthorizedViaConsent, AuthorizedVia(entry));
    }

    [Fact]
    public void ScopeProxy_AConsentedCallersRead_IsStamped()
    {
        var (proxy, audit) = ScopeProxy(Consented());

        proxy.Read(Team);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditEventType.ServiceCall, entry.EventType);
        Assert.Equal(AuditMetadataKeys.AuthorizedViaConsent, AuthorizedVia(entry));
    }

    [Fact]
    public void ScopeProxy_AMembersRead_CarriesNoAuthorizedVia()
    {
        var (proxy, audit) = ScopeProxy(Member());

        proxy.Read(Team);

        Assert.Null(AuthorizedVia(Assert.Single(audit.Entries)));
    }

    [Fact]
    public void ScopeProxy_WithConsentedAccessOff_AConsentedCallersChange_IsStillWritten()
    {
        var (proxy, audit) = ScopeProxy(Consented(), recordConsentedAccess: false);

        proxy.Change(Team);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditEventType.DataChange, entry.EventType);
        Assert.Equal(AuditMetadataKeys.AuthorizedViaConsent, AuthorizedVia(entry));
    }

    [Fact]
    public void ScopeProxy_WithConsentedAccessOff_AConsentedCallersRead_IsNotWritten()
    {
        var (proxy, audit) = ScopeProxy(Consented(), recordConsentedAccess: false);

        proxy.Read(Team);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public void ScopeProxy_WithConsentedAccessOff_AConsentedCallersRefusal_IsStillWritten()
    {
        var (proxy, audit) = ScopeProxy(Consented(scopes: []), recordConsentedAccess: false);

        Assert.Throws<UnauthorizedAccessException>(() => proxy.Read(Team));

        Assert.Equal(AuditEventType.ScopeDenial, Assert.Single(audit.Entries).EventType);
    }

    [Fact]
    public void ScopeProxy_WithConsentedAccessOff_AMembersRead_IsStillWritten()
    {
        var (proxy, audit) = ScopeProxy(Member(), recordConsentedAccess: false);

        proxy.Read(Team);

        Assert.Single(audit.Entries);
    }
}
