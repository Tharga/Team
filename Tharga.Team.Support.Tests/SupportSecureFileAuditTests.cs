using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// What secure files write to the audit trail: how many a reply or raise carried, who opened one, who was refused one and who removed
/// one; never a file's name, and no entry for opening an ordinary file.
/// </summary>
public class SupportSecureFileAuditTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";
    private const string Staff = "staff-subject";

    private static SupportAttachmentUpload Upload(string name)
        => new() { FileName = name, ContentType = "application/pdf", Content = new MemoryStream([1, 2, 3]) };

    [Fact]
    public async Task AReplyWithSecureFiles_IsAuditedWithHowManyOfEach_AndNeverTheirNames()
    {
        var world = new World();
        var alice = world.Member(Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Both.", null, [Upload("public.pdf")], [Upload("salaries-2026.xlsx"), Upload("contract.pdf")]);

        var entry = world.Sink.Entries.Last();
        Assert.Equal("reply", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("1", entry.Metadata[SupportAuditMetadataKeys.AttachmentCount]);
        Assert.Equal("2", entry.Metadata[SupportAuditMetadataKeys.SecureAttachmentCount]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains("salaries", StringComparison.Ordinal) || x.Contains("contract", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARaiseWithSecureFiles_IsAuditedWithHowMany_AndOneWithoutLeavesTheKeyOut()
    {
        var world = new World();
        var alice = world.Member(Alice);

        await alice.RaiseCaseAsync(TeamA, "Export", "See the file.", new SupportRaiseOptions { SecureAttachments = [Upload("salaries.xlsx")] });
        await alice.RaiseCaseAsync(TeamA, "Other", "Nothing attached.", new SupportRaiseOptions());

        Assert.Equal("1", world.Sink.Entries[0].Metadata[SupportAuditMetadataKeys.SecureAttachmentCount]);
        Assert.False(world.Sink.Entries[1].Metadata.ContainsKey(SupportAuditMetadataKeys.SecureAttachmentCount));
    }

    [Fact]
    public async Task OpeningASecureFile_IsAudited_WithItsIdAndNotItsName()
    {
        var world = new World();
        var (caseId, attachmentId) = await world.CaseWithASecureFileAsync();

        await using (await world.Staffer(SystemSupportScopes.AllRead).OpenAttachmentAsync(TeamA, caseId, 2, attachmentId)) { }

        var entry = world.Sink.Entries.Last();
        Assert.Equal("open-secure-file", entry.Action);
        Assert.True(entry.Success);
        Assert.Equal(caseId, entry.Metadata[SupportAuditMetadataKeys.CaseId]);
        Assert.Equal(attachmentId, entry.Metadata[SupportAuditMetadataKeys.AttachmentId]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains("salaries", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARefusedOpening_IsAudited_AsNotSucceeding()
    {
        var world = new World();
        var (caseId, attachmentId) = await world.CaseWithASecureFileAsync();

        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => world.Member(Bob, SupportScopes.Read).OpenAttachmentAsync(TeamA, caseId, 2, attachmentId));

        var entry = world.Sink.Entries.Last();
        Assert.Equal("open-secure-file", entry.Action);
        Assert.False(entry.Success);
        Assert.Equal(attachmentId, entry.Metadata[SupportAuditMetadataKeys.AttachmentId]);
    }

    [Fact]
    public async Task OpeningAnOrdinaryFile_IsNotAudited()
    {
        var world = new World();
        var alice = world.Member(Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");
        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Public.", [Upload("public.pdf")]);
        var before = world.Sink.Entries.Count;

        await using (await alice.OpenAttachmentAsync(TeamA, raised.Id, 2, Assert.Single(world.Files.Files).Attachment.Id)) { }

        Assert.Equal(before, world.Sink.Entries.Count);
    }

    [Fact]
    public async Task RemovingASecureFile_IsAudited_AndSoIsARefusedRemoval()
    {
        var world = new World();
        var (caseId, attachmentId) = await world.CaseWithASecureFileAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => world.Member(Bob, SupportScopes.Manage).RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId));
        await world.Staffer(SystemSupportScopes.AllManage).RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId);

        var removals = world.Sink.Entries.Where(x => x.Action == "remove-secure-file").ToArray();
        Assert.Equal([false, true], removals.Select(x => x.Success));
        Assert.All(removals, x => Assert.Equal(attachmentId, x.Metadata[SupportAuditMetadataKeys.AttachmentId]));
    }

    private sealed class World
    {
        private readonly InMemorySupportCaseStore _cases = new();

        public InMemorySupportAttachmentStore Files { get; } = new();

        public CollectingAuditLogger Sink { get; } = new();

        public ISupportCaseService Member(string subject, string teamScope = null)
            => Build(subject, TeamA, teamScope == null ? [] : [teamScope], []);

        public ISupportCaseService Staffer(string systemScope)
            => Build(Staff, null, [], [systemScope]);

        public async Task<(string CaseId, string AttachmentId)> CaseWithASecureFileAsync()
        {
            var alice = Member(Alice);
            var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");
            await alice.ReplyToCaseAsync(TeamA, raised.Id, "For support only.", null, [], [Upload("salaries.xlsx")]);

            return (raised.Id, Assert.Single(Files.Files).Attachment.Id);
        }

        private ISupportCaseService Build(string subject, string memberOfTeam, string[] teamScopes, string[] systemScopes)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, subject) };
            if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
            claims.AddRange(teamScopes.Select(s => new Claim(TeamClaimTypes.Scope, s)));
            claims.AddRange(systemScopes.Select(s => new Claim(TeamClaimTypes.SystemScope, s)));
            var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

            return new AuditingSupportCaseServiceDecorator(
                new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(_cases, authorizer, TimeProvider.System, attachmentStore: Files), authorizer),
                new CompositeAuditLogger([Sink], Options.Create(new AuditOptions())),
                new AuditEntryFactory(null));
        }
    }

    private sealed class CollectingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
