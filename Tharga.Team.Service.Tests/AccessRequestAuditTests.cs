using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Every access-request operation is audited with what was asked and decided — and so can be routed to Slack with the
/// existing notification routes.
/// </summary>
public class AccessRequestAuditTests
{
    private const string TeamKey = "team-1";

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public readonly List<AuditEntry> Entries = [];
        public void Log(AuditEntry entry) => Entries.Add(entry);
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private static (AuditingTeamServiceDecorator Sut, ITeamService Inner, RecordingAuditLogger Recorder) Build()
    {
        var inner = Substitute.For<ITeamService>();
        var recorder = new RecordingAuditLogger();
        var composite = new CompositeAuditLogger([recorder], Options.Create(new AuditOptions()));
        return (new AuditingTeamServiceDecorator(inner, composite, new HttpContextAccessor()), inner, recorder);
    }

    private static TeamAccessRequest Pending => new()
    {
        Id = "r1",
        RequesterKey = "dev-1",
        AccessLevel = AccessLevel.Administrator,
        Duration = TimeSpan.FromHours(8),
        RequestedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Requesting_RecordsTheRequest()
    {
        var (sut, inner, recorder) = Build();
        inner.RequestTeamAccessAsync(TeamKey, AccessLevel.Administrator, TimeSpan.FromHours(8), "why").Returns(Pending);

        await sut.RequestTeamAccessAsync(TeamKey, AccessLevel.Administrator, TimeSpan.FromHours(8), "why");

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal("request-access", entry.Action);
        Assert.Equal("r1", entry.Metadata[AuditMetadataKeys.AccessRequestId]);
        Assert.Equal("dev-1", entry.Metadata[AuditMetadataKeys.AccessRequestRequesterKey]);
        Assert.Equal("Administrator", entry.Metadata[AuditMetadataKeys.AccessRequestAccessLevel]);
        Assert.Equal("08:00:00", entry.Metadata[AuditMetadataKeys.AccessRequestDuration]);
    }

    [Fact]
    public async Task ARequestWithNoEnd_RecordsNone()
    {
        var (sut, inner, recorder) = Build();
        inner.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null).Returns(Pending with { Duration = null });

        await sut.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null);

        Assert.Equal("none", Assert.Single(recorder.Entries).Metadata[AuditMetadataKeys.AccessRequestDuration]);
    }

    /// <summary>A refused request is recorded too — attempts at access are what an operator looks for.</summary>
    [Fact]
    public async Task ARefusedRequest_IsRecordedAsFailed()
    {
        var (sut, inner, recorder) = Build();
        inner.RequestTeamAccessAsync(default, default, default, default).ReturnsForAnyArgs<TeamAccessRequest>(_ => throw new UnauthorizedAccessException("no role"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null));

        Assert.False(Assert.Single(recorder.Entries).Success);
    }

    /// <summary>
    /// The approval entry is the record of expiry — nothing runs when a window ends — so it names the consent level in
    /// force before, the roles consented, and when the granted consent ends.
    /// </summary>
    [Fact]
    public async Task Approving_RecordsTheConsentGrantedAndWhenItEnds()
    {
        var (sut, inner, recorder) = Build();
        var until = new DateTime(2026, 9, 14, 20, 0, 0, DateTimeKind.Utc);
        inner.GetTeamByKeyAsync(TeamKey).Returns(
            new TestTeam { Key = TeamKey, ConsentedRoles = ["Developer"], ConsentAccessLevel = AccessLevel.Viewer, AccessRequests = [Pending] },
            new TestTeam { Key = TeamKey, AccessRequests = [Pending with { Status = TeamAccessRequestStatus.Approved, GrantedUntil = until }] });

        await sut.ApproveTeamAccessRequestAsync(TeamKey, "r1", ["Developer"]);

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal("approve-access-request", entry.Action);
        Assert.Equal("dev-1", entry.Metadata[AuditMetadataKeys.AccessRequestRequesterKey]);
        Assert.Equal("Administrator", entry.Metadata[AuditMetadataKeys.AccessRequestAccessLevel]);
        Assert.Equal("Developer", entry.Metadata[AuditMetadataKeys.ConsentRoles]);
        Assert.Equal("Viewer", entry.Metadata[AuditMetadataKeys.ConsentAccessLevelOld]);
        Assert.Equal(until.ToString("O"), entry.Metadata[AuditMetadataKeys.AccessRequestGrantedUntil]);
    }

    [Theory]
    [InlineData("deny-access-request")]
    [InlineData("cancel-access-request")]
    public async Task DecidingOtherwise_RecordsTheRequest(string action)
    {
        var (sut, inner, recorder) = Build();
        inner.GetTeamByKeyAsync(TeamKey).Returns(new TestTeam { Key = TeamKey, AccessRequests = [Pending] });

        if (action == "deny-access-request") await sut.DenyTeamAccessRequestAsync(TeamKey, "r1");
        else await sut.CancelTeamAccessRequestAsync(TeamKey, "r1");

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(action, entry.Action);
        Assert.Equal("r1", entry.Metadata[AuditMetadataKeys.AccessRequestId]);
        Assert.Equal("dev-1", entry.Metadata[AuditMetadataKeys.AccessRequestRequesterKey]);
        Assert.False(entry.Metadata.ContainsKey(AuditMetadataKeys.ConsentRoles));
    }
}
