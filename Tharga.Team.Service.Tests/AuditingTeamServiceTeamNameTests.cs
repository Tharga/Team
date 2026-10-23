using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// Every audited member change carries the team's name next to its key (Tharga/Team#348), so a route can name the
/// team by what people call it. Reading the name must never fail the change it describes.
/// </summary>
public class AuditingTeamServiceTeamNameTests
{
    private const string TeamKey = "team-1";
    private const string UserKey = "user-1";

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public readonly List<AuditEntry> Entries = [];
        public void Log(AuditEntry entry) => Entries.Add(entry);
        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private static (AuditingTeamServiceDecorator Sut, ITeamService Inner, RecordingAuditLogger Recorder) Build(bool teamReadable = true)
    {
        var inner = Substitute.For<ITeamService>();
        if (teamReadable) inner.GetTeamByKeyAsync(TeamKey).Returns(new TestTeam { Key = TeamKey, Name = "Acme" });
        else inner.GetTeamByKeyAsync(TeamKey).Returns<Task<ITeam>>(_ => throw new InvalidOperationException("store down"));

        var recorder = new RecordingAuditLogger();
        var composite = new CompositeAuditLogger([recorder], Options.Create(new AuditOptions()));
        return (new AuditingTeamServiceDecorator(inner, composite, new HttpContextAccessor()), inner, recorder);
    }

    public static TheoryData<string, Func<AuditingTeamServiceDecorator, Task>> MemberChanges() => new()
    {
        { "invite", s => s.AddMemberAsync(TeamKey, new InviteUserModel { Email = "a@a.a" }) },
        { "remove-member", s => s.RemoveMemberAsync(TeamKey, UserKey) },
        { "leave-team", s => s.LeaveTeamAsync(TeamKey) },
        { "suspend", s => s.SetMemberSuspendedAsync(TeamKey, UserKey, true) },
        { "restore", s => s.SetMemberSuspendedAsync(TeamKey, UserKey, false) },
        { "set-role", s => s.SetMemberRoleAsync(TeamKey, UserKey, AccessLevel.User) },
        { "extend-invitation", s => s.ExtendInvitationAsync(TeamKey, "invite-code") },
        { "transfer-ownership", s => s.TransferOwnershipAsync<TestMember>(TeamKey, UserKey) },
    };

    [Theory]
    [MemberData(nameof(MemberChanges))]
    public async Task AMemberChange_RecordsTheTeamName(string action, Func<AuditingTeamServiceDecorator, Task> change)
    {
        var (sut, _, recorder) = Build();

        await change(sut);

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(action, entry.Action);
        Assert.Equal("Acme", entry.Metadata[AuditMetadataKeys.TeamName]);
    }

    [Theory]
    [MemberData(nameof(MemberChanges))]
    public async Task AFailedMemberChange_RecordsTheTeamNameToo(string action, Func<AuditingTeamServiceDecorator, Task> change)
    {
        var (sut, inner, recorder) = Build();
        inner.When(x => x.AddMemberAsync(Arg.Any<string>(), Arg.Any<InviteUserModel>())).Throw(new InvalidOperationException("no"));
        inner.When(x => x.RemoveMemberAsync(Arg.Any<string>(), Arg.Any<string>())).Throw(new InvalidOperationException("no"));
        inner.When(x => x.LeaveTeamAsync(Arg.Any<string>())).Throw(new InvalidOperationException("no"));
        inner.When(x => x.SetMemberSuspendedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())).Throw(new InvalidOperationException("no"));
        inner.When(x => x.SetMemberRoleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<AccessLevel>())).Throw(new InvalidOperationException("no"));
        inner.When(x => x.ExtendInvitationAsync(Arg.Any<string>(), Arg.Any<string>())).Throw(new InvalidOperationException("no"));
        inner.When(x => x.TransferOwnershipAsync<TestMember>(Arg.Any<string>(), Arg.Any<string>())).Throw(new InvalidOperationException("no"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => change(sut));

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(action, entry.Action);
        Assert.False(entry.Success);
        Assert.Equal("Acme", entry.Metadata[AuditMetadataKeys.TeamName]);
    }

    [Theory]
    [MemberData(nameof(MemberChanges))]
    public async Task WhenTheTeamCannotBeRead_TheChangeStillHappensAndIsRecordedWithoutAName(string action, Func<AuditingTeamServiceDecorator, Task> change)
    {
        var (sut, _, recorder) = Build(teamReadable: false);

        await change(sut);

        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(action, entry.Action);
        Assert.True(entry.Success);
        Assert.Equal(TeamKey, entry.TeamKey);
        Assert.False(entry.Metadata?.ContainsKey(AuditMetadataKeys.TeamName) ?? false);
    }

    [Fact]
    public async Task TheExistingDetail_IsKeptAlongsideTheName()
    {
        var (sut, _, recorder) = Build();

        await sut.AddMemberAsync(TeamKey, new InviteUserModel { Email = "a@a.a" });

        var metadata = Assert.Single(recorder.Entries).Metadata;
        Assert.Equal("a@a.a", metadata[AuditMetadataKeys.MemberEmail]);
        Assert.Equal("Acme", metadata[AuditMetadataKeys.TeamName]);
    }
}
