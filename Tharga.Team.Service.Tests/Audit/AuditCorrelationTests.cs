using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// The id that pulls one unit of work back together.
/// </summary>
/// <remarks>
/// It was derived from <c>HttpContext.TraceIdentifier</c> parsed as a <see cref="Guid"/>. That identifier
/// is <c>{ConnectionId}:{RequestNumber:X8}</c>, so the parse failed on every request and every entry fell
/// through to its own new id — measured on a consumer's database as 417 entries with 417 distinct ids
/// (Tharga/Team#260). These pin the replacement, and the fact that all three writers use it.
/// </remarks>
public class AuditCorrelationTests
{
    [Fact]
    public void TwoEntriesInOneActivity_ShareACorrelationId()
    {
        using var activity = StartActivity();
        var sut = new AuditEntryFactory(NoHttpContext());

        var first = sut.Create("case", "CaseClosed");
        var second = sut.Create(AuditEventType.DataChange, "case", "CaseReclassified");

        Assert.Equal(first.CorrelationId, second.CorrelationId);
    }

    /// <summary>
    /// The trace id itself, not a hash of it — sixteen bytes either way — so an entry can be matched
    /// against the request's spans in a telemetry tool.
    /// </summary>
    [Fact]
    public void TheCorrelationIdIsTheTraceId()
    {
        using var activity = StartActivity();
        var sut = new AuditEntryFactory(NoHttpContext());

        var entry = sut.Create("case", "CaseClosed");

        Assert.Equal(Guid.ParseExact(activity.TraceId.ToHexString(), "N"), entry.CorrelationId);
    }

    /// <summary>
    /// The regression this file exists for. Before the fix this was the behaviour on <i>every</i> request,
    /// not only outside one.
    /// </summary>
    [Fact]
    public void WithoutAnActivity_EachEntryStillGetsItsOwnId()
    {
        var sut = new AuditEntryFactory(NoHttpContext());

        var first = sut.Create("case", "CaseClosed");
        var second = sut.Create("case", "CaseClosed");

        Assert.NotEqual(Guid.Empty, first.CorrelationId);
        Assert.NotEqual(first.CorrelationId, second.CorrelationId);
    }

    /// <summary>Background work states its own grouping, and keeps it under an ambient trace.</summary>
    [Fact]
    public void ADeclaredCorrelationId_WinsOverTheActivity()
    {
        using var activity = StartActivity();
        var context = new AuditContextAccessor();
        var declared = Guid.NewGuid();
        var sut = new AuditEntryFactory(NoHttpContext());

        using (context.Push(new AuditActor("nightly-retention", CorrelationId: declared)))
        {
            Assert.Equal(declared, sut.Create("retention", "sweep").CorrelationId);
        }
    }

    /// <summary>
    /// The same precedence identity already uses: a real principal wins, so a scope left open on a pooled
    /// thread cannot pull a person's entries into a job's group.
    /// </summary>
    [Fact]
    public void AnAuthenticatedCaller_IgnoresAStrayScope()
    {
        using var activity = StartActivity();
        var context = new AuditContextAccessor();
        var sut = new AuditEntryFactory(AuthenticatedHttpContext());

        using (context.Push(new AuditActor("nightly-retention", CorrelationId: Guid.NewGuid())))
        {
            Assert.Equal(Guid.ParseExact(activity.TraceId.ToHexString(), "N"), sut.Create("case", "CaseClosed").CorrelationId);
        }
    }

    /// <summary>
    /// <b>The pair the grouping exists to join.</b> <c>ScopeProxy</c> builds its entry inline rather than
    /// through the factory, so this is what proves a fix in one writer reached the other — the consumer's
    /// domain entry and the access trace for the same call, in one request.
    /// </summary>
    [Fact]
    public async Task AProxyTraceAndAConsumerEntry_ShareTheRequestsCorrelationId()
    {
        using var activity = StartActivity();
        var (logger, backend) = FakeAuditLoggerFactory.Create();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(TeamClaimTypes.TeamKey, "team-1"), new Claim(TeamClaimTypes.Scope, AuditScopes.Read)], "Test"));

        var accessor = Substitute.For<ITeamPrincipalAccessor>();
        accessor.GetCurrentAsync().Returns(new ValueTask<ClaimsPrincipal>(principal));

        var composite = Substitute.For<CompositeAuditLogger>(
            (IEnumerable<IAuditLogger>)[], Options.Create(new AuditOptions()), null, null);
        composite.QueryAsync(Arg.Any<AuditQuery>()).Returns(new AuditQueryResult());

        var proxied = ScopeProxy<IAuditReadService>.Create(
            new AuditReadService(composite), accessor, ServiceScopeKind.Team, logger);

        await proxied.QueryAsync("team-1", new AuditQuery());
        var consumerEntry = new AuditEntryFactory(NoHttpContext()).Create(AuditEventType.DataChange, "case", "CaseClosed");

        var trace = Assert.Single(backend.Entries);
        Assert.Equal(consumerEntry.CorrelationId, trace.CorrelationId);
    }

    private static Activity StartActivity()
    {
        var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        return activity;
    }

    private static IHttpContextAccessor NoHttpContext()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext)null);
        return accessor;
    }

    private static IHttpContextAccessor AuthenticatedHttpContext()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone@example.com")], "Cookies"))
        });
        return accessor;
    }
}
