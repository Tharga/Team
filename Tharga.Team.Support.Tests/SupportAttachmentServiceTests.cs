using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Answering and replying with files, and opening a file under exactly the grants that read the case.
/// </summary>
/// <remarks>
/// <b>Opening a file is reading the case.</b> Every path that may read a transcript may open its files and no
/// other: the team's support read grant, the unassigned read grant for a case with no team, the cross-team
/// grant for staff, the author, and per-case visibility applied on top. An attachment id quoted against the
/// wrong case or entry finds nothing.
/// </remarks>
public class SupportAttachmentServiceTests
{
    private const string TeamA = "team-a";
    private const string TeamB = "team-b";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";
    private const string Staff = "staff-subject";

    private static readonly SupportAttachmentLimits SmallLimits = new() { MaxFileBytes = 10, MaxTotalBytes = 15, MaxCount = 2 };

    private static SupportAttachmentUpload Upload(string name, int size = 3, string contentType = "application/pdf")
        => new() { FileName = name, ContentType = contentType, Content = new MemoryStream(Enumerable.Repeat((byte)1, size).ToArray()) };

    private static SupportAttachmentUpload Unsized(string name, int size)
        => new() { FileName = name, ContentType = "application/octet-stream", Content = new UnsizedStream(new byte[size]) };

    [Fact]
    public async Task TeamSupport_AnswersWithFiles_AndTheAnswerCarriesThem()
    {
        var (cases, files) = Stores();
        var raised = await Member(cases, files, Alice).RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Member(cases, files, Bob, SupportScopes.Manage).AnswerCaseAsync(TeamA, raised.Id, "Here is the fixed export.", [Upload("export.csv", 4, "text/csv")]);

        var answer = (await Member(cases, files, Bob, SupportScopes.Manage).GetMessagesAsync(TeamA, raised.Id)).Items[^1];
        var attachment = Assert.Single(answer.Attachments);
        Assert.True(answer.AsSupport);
        Assert.Equal("export.csv", attachment.FileName);
        Assert.Equal("text/csv", attachment.ContentType);
        Assert.Equal(4, attachment.Size);
        Assert.Equal(raised.Id, Assert.Single(files.Files).CaseId);
    }

    [Fact]
    public async Task TheAuthor_RepliesWithFiles_OnTheSite()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "", [Upload("screenshot.png", 2, "image/png")]);

        var reply = (await alice.GetMessagesAsync(TeamA, raised.Id)).Items[^1];
        Assert.False(reply.AsSupport);
        Assert.Equal("screenshot.png", Assert.Single(reply.Attachments).FileName);
    }

    [Fact]
    public async Task StaffAnswerAnUnassignedCaseWithFiles_UnderTheUnassignedGrant()
    {
        var (cases, files) = Stores();
        await cases.AddCaseAsync(Unassigned(), First());

        await Staffer(cases, files, SystemSupportScopes.Manage).AnswerCaseAsync(null, "case-unassigned", "Attached.", [Upload("guide.pdf")]);

        Assert.Equal("case-unassigned", Assert.Single(files.Files).CaseId);
    }

    [Fact]
    public async Task TheAuthor_CannotAnswerAsSupport_WithFilesEither()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => alice.AnswerCaseAsync(TeamA, raised.Id, "Done.", [Upload("x.pdf")]));
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task ANameAndTypeFromTheCaller_AreMadeSafeBeforeTheyAreKept()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await alice.ReplyToCaseAsync(TeamA, raised.Id, "See.", [Upload("..\\..\\evil\\re‮port.pdf", 1, "text/html; name=\"x\"")]);

        var stored = Assert.Single(files.Files).Attachment;
        Assert.Equal("report.pdf", stored.FileName);
        Assert.Equal("text/html", stored.ContentType);
    }

    [Fact]
    public async Task MoreFilesThanAllowed_AreRefused_BeforeAnythingIsStored()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice, limits: SmallLimits);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => alice.ReplyToCaseAsync(TeamA, raised.Id, "Three.", [Upload("1"), Upload("2"), Upload("3")]));

        Assert.Empty(files.Files);
        Assert.Single((await alice.GetMessagesAsync(TeamA, raised.Id)).Items);
    }

    [Fact]
    public async Task AFileDeclaringTooMuch_IsRefused_BeforeAnythingIsStored()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice, limits: SmallLimits);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => alice.ReplyToCaseAsync(TeamA, raised.Id, "Big.", [Upload("big.zip", 11)]));

        Assert.Contains("big.zip", refusal.Message);
        Assert.Empty(files.Files);
    }

    /// <summary>A stream that cannot say its size is measured as it is stored, and what was already stored for the reply goes again.</summary>
    [Fact]
    public async Task AFileThatTurnsOutTooLarge_IsRefused_AndTheFilesBeforeItAreRemoved()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice, limits: SmallLimits);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => alice.ReplyToCaseAsync(TeamA, raised.Id, "Two.", [Upload("fine.pdf", 2), Unsized("secretly-huge.bin", 11)]));

        Assert.Contains("secretly-huge.bin", refusal.Message);
        Assert.Empty(files.Files);
        Assert.Single(files.Deleted);
        Assert.Single((await alice.GetMessagesAsync(TeamA, raised.Id)).Items);
    }

    [Fact]
    public async Task FilesThatTurnOutTooLargeTogether_AreRefused()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice, limits: SmallLimits);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => alice.ReplyToCaseAsync(TeamA, raised.Id, "Two.", [Unsized("a.bin", 9), Unsized("b.bin", 9)]));

        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task WithNoStore_FilesAreRefused_AndNothingIsWritten()
    {
        var cases = new InMemorySupportCaseStore();
        var alice = Member(cases, null, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => alice.ReplyToCaseAsync(TeamA, raised.Id, "See.", [Upload("x.pdf")]));

        Assert.Single((await alice.GetMessagesAsync(TeamA, raised.Id)).Items);
    }

    [Fact]
    public async Task AnEntryThatCouldNotBeWritten_TakesItsFilesWithIt()
    {
        var files = new InMemorySupportAttachmentStore();
        var cases = Substitute.For<ISupportCaseStore>();
        cases.GetCaseAsync(TeamA, "case-1", Arg.Any<CancellationToken>()).Returns(new SupportCase
        {
            Id = "case-1", TeamKey = TeamA, AuthorIdentity = Alice, AuthorName = "Alice", Subject = "S",
            Status = SupportCaseStatus.Open, CreatedAt = DateTime.UtcNow, MessageCount = 1
        });
        cases.AppendMessageAsync(TeamA, "case-1", Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("Full.")));

        var service = new SupportCaseService(cases, Authorizer(Alice, TeamA), TimeProvider.System, attachmentStore: files);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplyToCaseAsync(TeamA, "case-1", "See.", [Upload("x.pdf")]));

        Assert.Empty(files.Files);
        Assert.Single(files.Deleted);
    }

    [Fact]
    public async Task TheAuthor_OpensAFileOfTheirOwnCase()
    {
        var (cases, files, caseId, sequence, attachmentId) = await CaseWithAFileAsync();

        await using var opened = await Member(cases, files, Alice).OpenAttachmentAsync(TeamA, caseId, sequence, attachmentId);

        Assert.Equal("report.pdf", opened.Attachment.FileName);
        using var read = new MemoryStream();
        await opened.Content.CopyToAsync(read);
        Assert.Equal(3, read.Length);
    }

    [Fact]
    public async Task ATeamSupportReader_OpensAFileOfSomebodyElsesCase()
    {
        var (cases, files, caseId, sequence, attachmentId) = await CaseWithAFileAsync();

        await using var opened = await Member(cases, files, Bob, SupportScopes.Read).OpenAttachmentAsync(TeamA, caseId, sequence, attachmentId);

        Assert.NotNull(opened);
    }

    [Fact]
    public async Task AnotherMemberWithoutAGrant_CannotOpenIt()
    {
        var (cases, files, caseId, sequence, attachmentId) = await CaseWithAFileAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Member(cases, files, Bob).OpenAttachmentAsync(TeamA, caseId, sequence, attachmentId));
    }

    [Fact]
    public async Task AMemberOfAnotherTeam_CannotOpenIt_EvenWithTheirOwnTeamsGrant()
    {
        var (cases, files, caseId, sequence, attachmentId) = await CaseWithAFileAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Build(cases, files, Bob, memberOfTeam: TeamB, teamScopes: [SupportScopes.Read]).OpenAttachmentAsync(TeamA, caseId, sequence, attachmentId));
    }

    [Fact]
    public async Task StaffAcrossTeams_OpenAFileInATeamTheyAreNoMemberOf()
    {
        var (cases, files, caseId, sequence, attachmentId) = await CaseWithAFileAsync();

        await using var opened = await Staffer(cases, files, SystemSupportScopes.AllRead).OpenAttachmentAsync(TeamA, caseId, sequence, attachmentId);

        Assert.NotNull(opened);
    }

    /// <summary>A private case is the author's alone; the team's read grant does not reach its files.</summary>
    [Fact]
    public async Task APrivateCasesFiles_AreNotOpenedByTheTeamsReader()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice, mode: SupportCaseVisibilityMode.PerCase);
        var raised = await alice.RaiseCaseAsync(TeamA, "Private", "Mine.");
        await alice.ReplyToCaseAsync(TeamA, raised.Id, "File.", [Upload("private.pdf")]);
        var stored = Assert.Single(files.Files).Attachment;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Member(cases, files, Bob, SupportScopes.Read, mode: SupportCaseVisibilityMode.PerCase).OpenAttachmentAsync(TeamA, raised.Id, 2, stored.Id));
        await using var own = await alice.OpenAttachmentAsync(TeamA, raised.Id, 2, stored.Id);
        Assert.NotNull(own);
    }

    [Fact]
    public async Task AnUnassignedCasesFile_OpensUnderTheUnassignedReadGrant_AndNotUnderTheCrossTeamOne()
    {
        var (cases, files) = Stores();
        await cases.AddCaseAsync(Unassigned(), First());
        await Staffer(cases, files, SystemSupportScopes.Manage).AnswerCaseAsync(null, "case-unassigned", "Attached.", [Upload("guide.pdf")]);
        var id = Assert.Single(files.Files).Attachment.Id;

        await using var opened = await Staffer(cases, files, SystemSupportScopes.Read).OpenAttachmentAsync(null, "case-unassigned", 2, id);
        Assert.NotNull(opened);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staffer(cases, files, SystemSupportScopes.AllRead).OpenAttachmentAsync(null, "case-unassigned", 2, id));
    }

    [Fact]
    public async Task AnIdFromAnotherCase_FindsNothing()
    {
        var (cases, files, caseId, sequence, attachmentId) = await CaseWithAFileAsync();
        var alice = Member(cases, files, Alice);
        var other = await alice.RaiseCaseAsync(TeamA, "Other", "Another case.");
        await alice.ReplyToCaseAsync(TeamA, other.Id, "Its own file.", [Upload("other.pdf")]);

        Assert.Null(await alice.OpenAttachmentAsync(TeamA, other.Id, sequence, attachmentId));
        await using var own = await alice.OpenAttachmentAsync(TeamA, caseId, sequence, attachmentId);
        Assert.NotNull(own);
    }

    [Fact]
    public async Task AnIdNamedAgainstTheWrongEntry_FindsNothing()
    {
        var (cases, files, caseId, _, attachmentId) = await CaseWithAFileAsync();

        Assert.Null(await Member(cases, files, Alice).OpenAttachmentAsync(TeamA, caseId, 1, attachmentId));
    }

    [Fact]
    public async Task AnIdTheEntryDoesNotCarry_FindsNothing()
    {
        var (cases, files, caseId, sequence, _) = await CaseWithAFileAsync();

        Assert.Null(await Member(cases, files, Alice).OpenAttachmentAsync(TeamA, caseId, sequence, "not-an-attachment"));
    }

    [Fact]
    public async Task TheLimits_AreAskedOfTheService_ByAnyone()
    {
        var limits = await Build(new InMemorySupportCaseStore(), new InMemorySupportAttachmentStore(), Bob, limits: SmallLimits).GetAttachmentLimitsAsync();

        Assert.Equal(SmallLimits, limits);
    }

    private static async Task<(InMemorySupportCaseStore Cases, InMemorySupportAttachmentStore Files, string CaseId, int Sequence, string AttachmentId)> CaseWithAFileAsync()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");
        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Report attached.", [Upload("report.pdf")]);

        return (cases, files, raised.Id, 2, Assert.Single(files.Files).Attachment.Id);
    }

    private static (InMemorySupportCaseStore, InMemorySupportAttachmentStore) Stores() => (new InMemorySupportCaseStore(), new InMemorySupportAttachmentStore());

    private static SupportCase Unassigned() => new()
    {
        Id = "case-unassigned",
        AuthorIdentity = null,
        AuthorName = "stranger@example.com",
        Subject = "Cannot sign in",
        Status = SupportCaseStatus.Open,
        CreatedAt = DateTime.UtcNow,
        MessageCount = 1
    };

    private static SupportMessage First() => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorName = "stranger@example.com",
        Body = "It says my key expired.",
        SentAt = DateTime.UtcNow
    };

    private static ISupportCaseService Member(InMemorySupportCaseStore cases, InMemorySupportAttachmentStore files, string subject, string teamScope = null, SupportCaseVisibilityMode mode = SupportCaseVisibilityMode.TeamWide, SupportAttachmentLimits limits = null)
        => Build(cases, files, subject, memberOfTeam: TeamA, teamScopes: teamScope == null ? [] : [teamScope], mode: mode, limits: limits);

    private static ISupportCaseService Staffer(InMemorySupportCaseStore cases, InMemorySupportAttachmentStore files, string systemScope)
        => Build(cases, files, Staff, systemScopes: [systemScope]);

    private static ISupportCaseService Build(
        ISupportCaseStore cases,
        InMemorySupportAttachmentStore files,
        string subject,
        string memberOfTeam = null,
        string[] teamScopes = null,
        string[] systemScopes = null,
        SupportCaseVisibilityMode mode = SupportCaseVisibilityMode.TeamWide,
        SupportAttachmentLimits limits = null)
    {
        var authorizer = Authorizer(subject, memberOfTeam, teamScopes, systemScopes);

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(cases, authorizer, TimeProvider.System, visibilityMode: mode, attachmentStore: files, attachmentLimits: limits),
            authorizer,
            mode);
    }

    private static TeamAuthorizer Authorizer(string subject, string memberOfTeam = null, string[] teamScopes = null, string[] systemScopes = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject)
        };

        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange((teamScopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));

        return new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    private sealed class UnsizedStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
