using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// Two defects found while building per-method audit control (Tharga/Team#262), both of which made the
/// audit log wrong about refusals rather than merely quiet.
/// </summary>
public class AuditDenialRecordingTests
{
    /// <summary>
    /// <b>Every access-level denial was discarded before reaching any backend.</b>
    /// <c>CompositeAuditLogger.ShouldLog</c> maps an entry's event type onto an
    /// <see cref="AuditEventFilter"/> flag, and had no case for
    /// <see cref="AuditEventType.AccessLevelDenial"/> — so it fell through to
    /// <see cref="AuditEventFilter.None"/>, matched nothing, and was dropped on every host whatever the
    /// configuration said. <see cref="AuditEventFilter.Denials"/> existed and was in
    /// <see cref="AuditEventFilter.All"/>; it simply never matched this type.
    /// </summary>
    [Fact]
    public void AnAccessLevelDenial_ReachesTheBackend()
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create();

        logger.Log(Entry(AuditEventType.AccessLevelDenial));

        Assert.Single(backend.Entries);
    }

    [Fact]
    public void AScopeDenial_ReachesTheBackend()
    {
        var (logger, backend) = FakeAuditLoggerFactory.Create();

        logger.Log(Entry(AuditEventType.ScopeDenial));

        Assert.Single(backend.Entries);
    }

    /// <summary>Both denial kinds answer to the same filter flag, so a host can turn them off together.</summary>
    [Theory]
    [InlineData(AuditEventType.ScopeDenial)]
    [InlineData(AuditEventType.AccessLevelDenial)]
    public void ADenialIsSuppressedByTheDenialsFlag_NotBySomethingElse(AuditEventType eventType)
    {
        var options = new AuditOptions { EventFilter = AuditEventFilter.All & ~AuditEventFilter.Denials };
        var (logger, backend) = FakeAuditLoggerFactory.Create(options);

        logger.Log(Entry(eventType));

        Assert.Empty(backend.Entries);
    }

    /// <summary>
    /// The precedence this feature ships with: the attribute decides whether an entry is produced, and the
    /// host's existing filters still decide whether it is kept. Two rules, each doing one job.
    /// </summary>
    [Fact]
    public void AHostFilterStillDropsAnAccessTrace_TheAttributeAskedFor()
    {
        var options = new AuditOptions { EventFilter = AuditEventFilter.All & ~AuditEventFilter.ServiceCalls };
        var (logger, backend) = FakeAuditLoggerFactory.Create(options);

        logger.Log(Entry(AuditEventType.ServiceCall));

        Assert.Empty(backend.Entries);
    }

    /// <summary>
    /// And the escape hatch that follows from it, rather than from a special case: a method marked
    /// <c>AuditMode.Change</c> records a <see cref="AuditEventType.DataChange"/>, which survives a host
    /// filter that drops service calls. A read the law requires recorded can therefore be recorded in a
    /// host that has otherwise silenced access tracing.
    /// </summary>
    [Fact]
    public void AChangeSurvivesAFilterThatDropsServiceCalls()
    {
        var options = new AuditOptions { EventFilter = AuditEventFilter.All & ~AuditEventFilter.ServiceCalls };
        var (logger, backend) = FakeAuditLoggerFactory.Create(options);

        logger.Log(Entry(AuditEventType.DataChange));

        Assert.Single(backend.Entries);
    }

    private static AuditEntry Entry(AuditEventType eventType) =>
        new()
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Feature = "case",
            Action = "read",
            CallerSource = AuditCallerSource.Web
        };
}
