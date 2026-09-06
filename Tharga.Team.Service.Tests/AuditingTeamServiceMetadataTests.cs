using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Asserts that each audited team operation records what actually changed (#129), including the
/// before/after pairs for the operations where the previous value earns its extra read.
/// </summary>
public class AuditingTeamServiceMetadataTests
{
    private const string TeamKey = "team-1";
    private const string UserKey = "user-1";

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

    private static async IAsyncEnumerable<T> Stream<T>(params T[] items)
    {
        await Task.CompletedTask;
        foreach (var item in items) yield return item;
    }

    private static IReadOnlyDictionary<string, string> Single(RecordingAuditLogger recorder)
        => Assert.Single(recorder.Entries).Metadata;

    [Fact]
    public async Task Create_RecordsTheResolvedTeamName()
    {
        var (sut, inner, recorder) = Build();
        inner.CreateTeamAsync("Acme").Returns(new TestTeam { Key = TeamKey, Name = "Acme" });

        await sut.CreateTeamAsync("Acme");

        Assert.Equal("Acme", Single(recorder)[AuditMetadataKeys.TeamName]);
    }

    [Fact]
    public async Task Rename_RecordsOldAndNewName()
    {
        var (sut, inner, recorder) = Build();
        inner.GetTeamAsync<TestMember>(TeamKey).Returns(new TestTeam { Key = TeamKey, Name = "Acme Ltd" });

        await sut.RenameTeamAsync<TestMember>(TeamKey, "Acme Group");

        var metadata = Single(recorder);
        Assert.Equal("Acme Ltd", metadata[AuditMetadataKeys.TeamNameOld]);
        Assert.Equal("Acme Group", metadata[AuditMetadataKeys.TeamNameNew]);
    }

    /// <summary>
    /// A "before" read that fails must omit its key rather than record a misleading null — and must not
    /// stop the operation or the audit entry.
    /// </summary>
    [Fact]
    public async Task Rename_WhenTheBeforeReadThrows_StillLogsWithoutTheOldValue()
    {
        var (sut, inner, recorder) = Build();
        inner.GetTeamAsync<TestMember>(TeamKey).Returns<Task<ITeam<TestMember>>>(_ => throw new InvalidOperationException("boom"));

        await sut.RenameTeamAsync<TestMember>(TeamKey, "Acme Group");

        var metadata = Single(recorder);
        Assert.DoesNotContain(AuditMetadataKeys.TeamNameOld, metadata.Keys);
        Assert.Equal("Acme Group", metadata[AuditMetadataKeys.TeamNameNew]);
    }

    /// <summary>The name is unrecoverable after deletion, which is what earns the read.</summary>
    [Fact]
    public async Task Delete_RecordsTheTeamName()
    {
        var (sut, inner, recorder) = Build();
        inner.GetTeamAsync<TestMember>(TeamKey).Returns(new TestTeam { Key = TeamKey, Name = "Acme Ltd" });

        await sut.DeleteTeamAsync<TestMember>(TeamKey);

        Assert.Equal("Acme Ltd", Single(recorder)[AuditMetadataKeys.TeamName]);
    }

    [Fact]
    public async Task Invite_RecordsTheEmail_WithoutAnExtraRead()
    {
        var (sut, _, recorder) = Build();

        await sut.AddMemberAsync(TeamKey, new InviteUserModel { Email = "new@example.com" });

        Assert.Equal("new@example.com", Single(recorder)[AuditMetadataKeys.MemberEmail]);
    }

    [Fact]
    public async Task RemoveMember_RecordsTheMemberKey()
    {
        var (sut, _, recorder) = Build();

        await sut.RemoveMemberAsync(TeamKey, UserKey);

        Assert.Equal(UserKey, Single(recorder)[AuditMetadataKeys.MemberKey]);
    }

    [Fact]
    public async Task SetMemberRole_RecordsOldAndNewAccessLevel()
    {
        var (sut, inner, recorder) = Build();
        inner.GetMembersAsync(TeamKey).Returns(Stream<ITeamMember>(new TestMember { Key = UserKey, AccessLevel = AccessLevel.Viewer }));

        await sut.SetMemberRoleAsync(TeamKey, UserKey, AccessLevel.Administrator);

        var metadata = Single(recorder);
        Assert.Equal(nameof(AccessLevel.Viewer), metadata[AuditMetadataKeys.MemberAccessLevelOld]);
        Assert.Equal(nameof(AccessLevel.Administrator), metadata[AuditMetadataKeys.MemberAccessLevelNew]);
    }

    /// <summary>
    /// Clearing a display-name override records an empty string, which stays distinguishable from a
    /// failed read (key omitted entirely).
    /// </summary>
    [Fact]
    public async Task SetMemberName_ClearingAnOverride_RecordsEmptyStringNotAMissingKey()
    {
        var (sut, inner, recorder) = Build();
        inner.GetMembersAsync(TeamKey).Returns(Stream<ITeamMember>(new TestMember { Key = UserKey, Name = "Old Name" }));

        await sut.SetMemberNameAsync(TeamKey, UserKey, null);

        var metadata = Single(recorder);
        Assert.Equal("Old Name", metadata[AuditMetadataKeys.MemberNameOld]);
        Assert.Equal("", metadata[AuditMetadataKeys.MemberNameNew]);
    }

    [Fact]
    public async Task SetConsent_RecordsOldAndNewLevelAndRoles()
    {
        var (sut, inner, recorder) = Build();
        inner.GetTeamByKeyAsync(TeamKey).Returns(new TestTeam { Key = TeamKey, ConsentAccessLevel = AccessLevel.Viewer });

        await sut.SetTeamConsentAsync(TeamKey, ["Developer", "Support"], AccessLevel.Administrator);

        var metadata = Single(recorder);
        Assert.Equal(nameof(AccessLevel.Viewer), metadata[AuditMetadataKeys.ConsentAccessLevelOld]);
        Assert.Equal(nameof(AccessLevel.Administrator), metadata[AuditMetadataKeys.ConsentAccessLevelNew]);
        Assert.Equal("Developer, Support", metadata[AuditMetadataKeys.ConsentRoles]);
    }

    /// <summary>Revoking consent is a fact worth recording, not an absent value.</summary>
    [Fact]
    public async Task SetConsent_WhenCleared_RecordsNoneRatherThanOmittingTheKey()
    {
        var (sut, inner, recorder) = Build();
        inner.GetTeamByKeyAsync(TeamKey).Returns(new TestTeam { Key = TeamKey, ConsentAccessLevel = AccessLevel.Administrator });

        await sut.SetTeamConsentAsync(TeamKey, [], null);

        var metadata = Single(recorder);
        Assert.Equal(nameof(AccessLevel.Administrator), metadata[AuditMetadataKeys.ConsentAccessLevelOld]);
        Assert.Equal("none", metadata[AuditMetadataKeys.ConsentAccessLevelNew]);
    }

    /// <summary>
    /// A non-member acting through consent (Administrator-level consent) can change a team's consent, but
    /// the team isn't among their own teams. The "before" value is now read by exact key, so it's recorded
    /// for them too — the previous own-teams scan found nothing and omitted it.
    /// </summary>
    [Fact]
    public async Task SetConsent_NonMemberCaller_StillRecordsOldValue()
    {
        var (sut, inner, recorder) = Build();
        inner.GetTeamsAsync().Returns(Stream<ITeam>());  // caller is not a member of this team
        inner.GetTeamByKeyAsync(TeamKey).Returns(new TestTeam { Key = TeamKey, ConsentAccessLevel = AccessLevel.Viewer });

        await sut.SetTeamConsentAsync(TeamKey, ["Developer"], AccessLevel.Administrator);

        Assert.Equal(nameof(AccessLevel.Viewer), Single(recorder)[AuditMetadataKeys.ConsentAccessLevelOld]);
    }

    [Fact]
    public async Task TransferOwnership_RecordsTheNewOwner()
    {
        var (sut, _, recorder) = Build();

        await sut.TransferOwnershipAsync<TestMember>(TeamKey, "user-2");

        Assert.Equal("user-2", Single(recorder)[AuditMetadataKeys.NewOwnerKey]);
    }

    /// <summary>
    /// Leaving and being removed are the same write to the roster and read very differently afterwards,
    /// so they are separate actions in the log.
    /// </summary>
    [Fact]
    public async Task Leave_IsRecordedApartFromRemoveMember()
    {
        var (sut, _, recorder) = Build();

        await sut.LeaveTeamAsync(TeamKey);

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal("leave-team", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal(TeamKey, entry.TeamKey);
    }

    [Fact]
    public async Task Leave_Refused_IsStillRecorded()
    {
        var (sut, inner, recorder) = Build();
        inner.LeaveTeamAsync(TeamKey).Returns<Task>(_ => throw new InvalidOperationException("The owner cannot leave the team."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.LeaveTeamAsync(TeamKey));

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal("leave-team", entry.Action);
        Assert.False(entry.Success);
    }

    /// <summary>A failed operation should still record what was attempted.</summary>
    [Fact]
    public async Task FailedOperation_StillRecordsTheAttemptedValues()
    {
        var (sut, inner, recorder) = Build();
        inner.GetTeamAsync<TestMember>(TeamKey).Returns(new TestTeam { Key = TeamKey, Name = "Acme Ltd" });
        inner.RenameTeamAsync<TestMember>(TeamKey, "Acme Group").Returns<Task>(_ => throw new UnauthorizedAccessException());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.RenameTeamAsync<TestMember>(TeamKey, "Acme Group"));

        var entry = Assert.Single(recorder.Entries);
        Assert.False(entry.Success);
        Assert.Equal("Acme Ltd", entry.Metadata[AuditMetadataKeys.TeamNameOld]);
        Assert.Equal("Acme Group", entry.Metadata[AuditMetadataKeys.TeamNameNew]);
    }
}
