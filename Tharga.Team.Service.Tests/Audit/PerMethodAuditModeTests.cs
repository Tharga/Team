using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

public interface IAuditModeScopeService
{
    [RequireScope("case:read")]
    string Unannotated(string teamKey);

    [RequireScope("case:read", Audit = AuditMode.None)]
    string Silent(string teamKey);

    [RequireScope("case:read", Audit = AuditMode.Access)]
    string Traced(string teamKey);

    [RequireScope("case:manage", Audit = AuditMode.Change)]
    string Changed(string teamKey);
}

public interface IAuditModeLevelService
{
    [RequireAccessLevel(AccessLevel.Viewer)]
    string Unannotated();

    [RequireAccessLevel(AccessLevel.Viewer, Audit = AuditMode.None)]
    string Silent();

    [RequireAccessLevel(AccessLevel.Administrator, Audit = AuditMode.None)]
    string SilentAndPrivileged();

    [RequireAccessLevel(AccessLevel.Viewer, Audit = AuditMode.Change)]
    string Changed();
}

/// <summary>
/// Per-method audit control on the enforcement attributes (Tharga/Team#262).
/// </summary>
/// <remarks>
/// The issue described this as making the proxies write. They already wrote for every intercepted call —
/// which is what #260 measured as 274 of 319 entries — so what this actually adds is the ability to stop.
/// Both proxies are exercised for real rather than through a helper, because the point of the feature is
/// that the two answer identically.
/// </remarks>
public class PerMethodAuditModeTests
{
    private const string Team = "team-1";

    // ---------------- ScopeProxy ----------------

    /// <summary>
    /// The regression guard for every existing consumer: configure nothing, annotate nothing, and the
    /// access trace is exactly what it has always been.
    /// </summary>
    [Fact]
    public void AnUnannotatedMethod_OnAnUnconfiguredHost_IsStillAudited()
    {
        var (service, backend) = ScopeService(AuditMode.Access);

        service.Unannotated(Team);

        var entry = Assert.Single(backend.Entries);
        Assert.Equal(AuditEventType.ServiceCall, entry.EventType);
    }

    [Fact]
    public void AHostThatDefaultsToSilence_RecordsNothingForAnUnannotatedMethod()
    {
        var (service, backend) = ScopeService(AuditMode.None);

        service.Unannotated(Team);

        Assert.Empty(backend.Entries);
    }

    /// <summary>The reason the host default is not the whole answer: some reads must be recorded.</summary>
    [Fact]
    public void AMethodAskingToBeTraced_IsRecordedEvenWhereTheHostDefaultsToSilence()
    {
        var (service, backend) = ScopeService(AuditMode.None);

        service.Traced(Team);

        var entry = Assert.Single(backend.Entries);
        Assert.Equal(AuditEventType.ServiceCall, entry.EventType);
    }

    [Fact]
    public void AMethodAskingForSilence_IsNotRecordedEvenWhereTheHostAudits()
    {
        var (service, backend) = ScopeService(AuditMode.Access);

        service.Silent(Team);

        Assert.Empty(backend.Entries);
    }

    /// <summary>
    /// A change records as <c>DataChange</c>, which is what lets the log view's Event filter separate what
    /// somebody did from what somebody was permitted to call.
    /// </summary>
    [Fact]
    public void AMethodMarkedAsAChange_RecordsADataChange()
    {
        var (service, backend) = ScopeService(AuditMode.None, "case:manage");

        service.Changed(Team);

        var entry = Assert.Single(backend.Entries);
        Assert.Equal(AuditEventType.DataChange, entry.EventType);
    }

    /// <summary>
    /// <b>Silence hides the read, never the refusal.</b> Suppressing this would quietly delete the evidence
    /// an audit log exists to hold.
    /// </summary>
    [Fact]
    public void ARefusal_IsRecordedEvenOnASilentMethod()
    {
        var (service, backend) = ScopeService(AuditMode.None, scopeHeld: null);

        Assert.Throws<UnauthorizedAccessException>(() => service.Silent(Team));

        var entry = Assert.Single(backend.Entries);
        Assert.Equal(AuditEventType.ScopeDenial, entry.EventType);
        Assert.Equal(AuditScopeResult.Denied, entry.ScopeResult);
    }

    /// <summary>
    /// <b>A refused team call used to be recorded as an allowed one.</b> The proxy classified denials by
    /// substring-matching the exception text for "Missing required scope" — wording only the *system* path
    /// produces. A team-scope refusal says "requires the '…' scope on that team", so it never matched: the
    /// entry was written as <see cref="AuditEventType.ServiceCall"/> with
    /// <see cref="AuditScopeResult.Allowed"/>, stating that a call which was refused had been permitted.
    /// </summary>
    [Fact]
    public void ARefusedTeamCall_IsNotRecordedAsAnAllowedOne()
    {
        var (service, backend) = ScopeService(AuditMode.Access, scopeHeld: null);

        Assert.Throws<UnauthorizedAccessException>(() => service.Unannotated(Team));

        var entry = Assert.Single(backend.Entries);
        Assert.Equal(AuditEventType.ScopeDenial, entry.EventType);
        Assert.Equal(AuditScopeResult.Denied, entry.ScopeResult);
        Assert.False(entry.Success);
    }

    // ---------------- AccessLevelProxy ----------------

    [Fact]
    public void TheAccessLevelProxy_AuditsAnUnannotatedMethodByDefault()
    {
        var (service, backend) = LevelService(AuditMode.Access);

        service.Unannotated();

        Assert.Single(backend.Entries);
    }

    [Fact]
    public void TheAccessLevelProxy_HonoursSilence()
    {
        var (service, backend) = LevelService(AuditMode.Access);

        service.Silent();

        Assert.Empty(backend.Entries);
    }

    [Fact]
    public void TheAccessLevelProxy_HonoursTheHostDefault()
    {
        var (service, backend) = LevelService(AuditMode.None);

        service.Unannotated();

        Assert.Empty(backend.Entries);
    }

    [Fact]
    public void TheAccessLevelProxy_RecordsAChangeAsADataChange()
    {
        var (service, backend) = LevelService(AuditMode.None);

        service.Changed();

        var entry = Assert.Single(backend.Entries);
        Assert.Equal(AuditEventType.DataChange, entry.EventType);
    }

    /// <summary>The same rule as the scope proxy, with that proxy's own denial event.</summary>
    [Fact]
    public void TheAccessLevelProxy_RecordsARefusalOnASilentMethod()
    {
        var (service, backend) = LevelService(AuditMode.None, level: "Viewer");

        Assert.Throws<UnauthorizedAccessException>(() => service.SilentAndPrivileged());

        var entry = Assert.Single(backend.Entries);
        Assert.Equal(AuditEventType.AccessLevelDenial, entry.EventType);
    }

    // ---------------- scaffolding ----------------

    private static (IAuditModeScopeService Service, FakeAuditBackend Backend) ScopeService(
        AuditMode hostDefault, string scopeHeld = "case:read")
    {
        var target = Substitute.For<IAuditModeScopeService>();
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        var proxy = ScopeProxy<IAuditModeScopeService>.Create(
            target, Principal(scopeHeld), ServiceScopeKind.Team, logger, hostDefault);

        return (proxy, backend);
    }

    private static (IAuditModeLevelService Service, FakeAuditBackend Backend) LevelService(
        AuditMode hostDefault, string level = "Administrator")
    {
        var target = Substitute.For<IAuditModeLevelService>();
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(TeamClaimTypes.TeamKey, Team), new Claim(TeamClaimTypes.AccessLevel, level)], "Test"))));

        return (AccessLevelProxy<IAuditModeLevelService>.Create(target, accessor, logger, hostDefault), backend);
    }

    private static ITeamPrincipalAccessor Principal(string scopeHeld)
    {
        var claims = new List<Claim> { new(TeamClaimTypes.TeamKey, Team) };
        if (scopeHeld != null) claims.Add(new Claim(TeamClaimTypes.Scope, scopeHeld));

        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))));
        return accessor;
    }
}
