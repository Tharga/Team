using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// What <c>SetOwnerAsync</c> writes to the audit log, and — just as much — what it deliberately does not.
/// </summary>
/// <remarks>
/// Two things here are easy to get wrong and invisible when you do.
/// <para>
/// <b>The demoted owners must be recorded.</b> The operation "works" without them, so the metadata is the
/// first thing a refactor drops; but "who lost ownership" is the whole security-relevant half of the event,
/// and it is a list rather than a value because a legacy team can carry several owners.
/// </para>
/// <para>
/// <b>A call that changed nothing must write nothing.</b> The intended caller is a sync running on a
/// schedule against teams that are already correct. Recording an entry on every pass would bury the real
/// events under thousands of non-events — and keying "did something happen" on the demoted list instead of
/// on <see cref="SetOwnerResult.Changed"/> would silently stop auditing the ownerless-repair case, which
/// demotes nobody yet is the entry most worth having.
/// </para>
/// </remarks>
public class SetOwnerAuditTests
{
    private const string TeamKey = "team-1";

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public readonly List<AuditEntry> Entries = [];
        public void Log(AuditEntry entry) => Entries.Add(entry);
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private static (AuditingTeamServiceDecorator sut, ITeamService inner, RecordingAuditLogger recorder) Build()
    {
        var inner = Substitute.For<ITeamService>();
        var recorder = new RecordingAuditLogger();
        var composite = new CompositeAuditLogger([recorder], Options.Create(new AuditOptions()));
        return (new AuditingTeamServiceDecorator(inner, composite, new HttpContextAccessor()), inner, recorder);
    }

    [Fact]
    public async Task ReducingSeveralOwners_RecordsEveryDemotedOwner()
    {
        var (sut, inner, recorder) = Build();
        inner.SetOwnerAsync<ITeamMember>(TeamKey, "owner-2")
            .Returns(new SetOwnerResult(true, ["owner-1", "owner-3"]));

        await sut.SetOwnerAsync<ITeamMember>(TeamKey, "owner-2");

        var metadata = Assert.Single(recorder.Entries).Metadata;
        Assert.Equal("owner-2", metadata[AuditMetadataKeys.NewOwnerKey]);
        Assert.Equal("owner-1,owner-3", metadata[AuditMetadataKeys.DemotedOwnerKeys]);
    }

    [Fact]
    public async Task TransferringFromOneOwner_RecordsTheDisplacedOwner()
    {
        var (sut, inner, recorder) = Build();
        inner.SetOwnerAsync<ITeamMember>(TeamKey, "admin-1")
            .Returns(new SetOwnerResult(true, ["owner-1"]));

        await sut.SetOwnerAsync<ITeamMember>(TeamKey, "admin-1");

        Assert.Equal("owner-1", Assert.Single(recorder.Entries).Metadata[AuditMetadataKeys.DemotedOwnerKeys]);
    }

    /// <summary>
    /// Repairing an ownerless team demotes nobody and is still an event. Keying the entry on the demoted
    /// list rather than on <c>Changed</c> would drop exactly this case.
    /// </summary>
    [Fact]
    public async Task RepairingAnOwnerlessTeam_IsStillAudited()
    {
        var (sut, inner, recorder) = Build();
        inner.SetOwnerAsync<ITeamMember>(TeamKey, "admin-1")
            .Returns(new SetOwnerResult(true, []));

        await sut.SetOwnerAsync<ITeamMember>(TeamKey, "admin-1");

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal("admin-1", entry.Metadata[AuditMetadataKeys.NewOwnerKey]);
        Assert.False(entry.Metadata.ContainsKey(AuditMetadataKeys.DemotedOwnerKeys));
    }

    [Fact]
    public async Task WhenNothingChanged_WritesNoEntry()
    {
        var (sut, inner, recorder) = Build();
        inner.SetOwnerAsync<ITeamMember>(TeamKey, "owner-1").Returns(SetOwnerResult.NoChange);

        await sut.SetOwnerAsync<ITeamMember>(TeamKey, "owner-1");

        Assert.Empty(recorder.Entries);
    }

    /// <summary>
    /// A refusal is recorded too. A rejected attempt on a team the caller has no business touching is
    /// exactly what taking one over would look like on the way in.
    /// </summary>
    [Fact]
    public async Task WhenTheOperationThrows_RecordsAFailure()
    {
        var (sut, inner, recorder) = Build();
        inner.SetOwnerAsync<ITeamMember>(TeamKey, "stranger")
            .Returns<SetOwnerResult>(_ => throw new InvalidOperationException("not a member"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetOwnerAsync<ITeamMember>(TeamKey, "stranger"));

        var entry = Assert.Single(recorder.Entries);
        Assert.False(entry.Success);
        Assert.Equal("stranger", entry.Metadata[AuditMetadataKeys.NewOwnerKey]);
    }

    [Fact]
    public async Task TheResultIsReturnedToTheCaller()
    {
        var (sut, inner, _) = Build();
        inner.SetOwnerAsync<ITeamMember>(TeamKey, "owner-2")
            .Returns(new SetOwnerResult(true, ["owner-1"]));

        var result = await sut.SetOwnerAsync<ITeamMember>(TeamKey, "owner-2");

        Assert.True(result.Changed);
        Assert.Equal(["owner-1"], result.DemotedOwnerKeys);
    }
}
