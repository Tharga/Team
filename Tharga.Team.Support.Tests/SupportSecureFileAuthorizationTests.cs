using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Who sees a secure file, enforced once in the decorator: the product's support staff and the member who uploaded it see it whole and
/// open it; everyone else reading the case, the team's own support readers included, sees it withheld and is refused it.
/// </summary>
public class SupportSecureFileAuthorizationTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";
    private const string Staff = "staff-subject";

    private static SupportAttachmentUpload Upload(string name, int size = 3)
        => new() { FileName = name, ContentType = "application/pdf", Content = new MemoryStream(Enumerable.Repeat((byte)1, size).ToArray()) };

    [Fact]
    public async Task TheUploader_SeesTheirSecureFileWhole_AndOpensIt()
    {
        var (cases, files, caseId, attachmentId) = await CaseWithASecureFileAsync();
        var alice = Member(cases, files, Alice);

        var file = SecureFileOf(await alice.GetMessagesAsync(TeamA, caseId));
        await using var opened = await alice.OpenAttachmentAsync(TeamA, caseId, 2, attachmentId);

        Assert.Equal("salaries.xlsx", file.FileName);
        Assert.False(file.Withheld);
        Assert.Equal(Alice, file.UploadedBy);
        Assert.Equal("salaries.xlsx", opened.Attachment.FileName);
    }

    [Theory]
    [InlineData(SystemSupportScopes.AllRead)]
    [InlineData(SystemSupportScopes.AllManage)]
    public async Task SupportStaff_SeeItWhole_AndOpenIt(string scope)
    {
        var (cases, files, caseId, attachmentId) = await CaseWithASecureFileAsync();
        var staff = Staffer(cases, files, scope);

        var file = SecureFileOf(await staff.GetMessagesAsync(TeamA, caseId));
        await using var opened = await staff.OpenAttachmentAsync(TeamA, caseId, 2, attachmentId);

        Assert.Equal("salaries.xlsx", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.False(file.Withheld);
        Assert.NotNull(opened);
    }

    /// <summary>The team's own support readers are the member's colleagues: they learn a file is there, and nothing about it.</summary>
    [Theory]
    [InlineData(SupportScopes.Read)]
    [InlineData(SupportScopes.Manage)]
    public async Task TheTeamsOwnSupportReaders_SeeItWithheld_AndAreRefusedIt(string scope)
    {
        var (cases, files, caseId, attachmentId) = await CaseWithASecureFileAsync();
        var bob = Member(cases, files, Bob, scope);

        var file = SecureFileOf(await bob.GetMessagesAsync(TeamA, caseId));

        Assert.True(file.Withheld);
        Assert.True(file.Secure);
        Assert.Equal(string.Empty, file.FileName);
        Assert.Equal(string.Empty, file.ContentType);
        Assert.Null(file.UploadedBy);
        Assert.Equal(attachmentId, file.Id);
        Assert.Equal(3, file.Size);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bob.OpenAttachmentAsync(TeamA, caseId, 2, attachmentId));
    }

    [Fact]
    public async Task AnOrdinaryFileBesideIt_IsNotWithheld()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");
        await alice.ReplyToCaseAsync(TeamA, raised.Id, "Both.", null, [Upload("public.pdf")], [Upload("salaries.xlsx")]);

        var attachments = (await Member(cases, files, Bob, SupportScopes.Read).GetMessagesAsync(TeamA, raised.Id)).Items[^1].Attachments;

        Assert.Equal("public.pdf", attachments.Single(x => !x.Secure).FileName);
        Assert.True(attachments.Single(x => x.Secure).Withheld);
    }

    /// <summary>The uploader, not the case's author, is who it opens for: a colleague's file on somebody's case is withheld from that somebody.</summary>
    [Fact]
    public async Task AColleaguesSecureFile_OnTheAuthorsCase_IsWithheldFromTheAuthor()
    {
        var (cases, files) = Stores();
        var raised = await Member(cases, files, Alice).RaiseCaseAsync(TeamA, "Export", "It is empty.");
        var bob = Member(cases, files, Bob, SupportScopes.Manage);
        await bob.ReplyToCaseAsync(TeamA, raised.Id, "Mine.", null, [], [Upload("bobs.xlsx")]);

        Assert.False(SecureFileOf(await bob.GetMessagesAsync(TeamA, raised.Id)).Withheld);
        Assert.True(SecureFileOf(await Member(cases, files, Alice).GetMessagesAsync(TeamA, raised.Id)).Withheld);
    }

    [Fact]
    public async Task ARemovedFile_StillShowsItWasRemoved_WhenWithheld()
    {
        var (cases, files, caseId, attachmentId) = await CaseWithASecureFileAsync();
        await Staffer(cases, files, SystemSupportScopes.AllManage).RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId);

        var file = SecureFileOf(await Member(cases, files, Bob, SupportScopes.Read).GetMessagesAsync(TeamA, caseId));

        Assert.True(file.Withheld);
        Assert.NotNull(file.RemovedAt);
    }

    /// <summary>Only the unassigned grant reaches a case no team owns, and whoever holds it is the product's support staff.</summary>
    [Fact]
    public async Task OnACaseNoTeamOwns_TheUnassignedReaders_SeeItWhole()
    {
        var (cases, files) = Stores();
        await cases.AddCaseAsync(Unassigned(), First() with
        {
            Attachments = [new SupportAttachment { Id = "a1", FileName = "salaries.xlsx", ContentType = "application/pdf", Size = 3, Secure = true, UploadedBy = Alice }]
        });

        var file = SecureFileOf(await Staffer(cases, files, SystemSupportScopes.Read).GetMessagesAsync(null, "case-unassigned"));

        Assert.False(file.Withheld);
        Assert.Equal("salaries.xlsx", file.FileName);
    }

    [Fact]
    public async Task StaffWithTheCrossTeamWriteGrant_RemoveIt()
    {
        var (cases, files, caseId, attachmentId) = await CaseWithASecureFileAsync();

        await Staffer(cases, files, SystemSupportScopes.AllManage).RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId);

        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task NobodyElse_RemovesIt_NotEvenTheUploader()
    {
        var (cases, files, caseId, attachmentId) = await CaseWithASecureFileAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staffer(cases, files, SystemSupportScopes.AllRead).RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staffer(cases, files, SystemSupportScopes.Manage).RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Member(cases, files, Bob, SupportScopes.Manage).RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Member(cases, files, Alice).RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId));

        Assert.Single(files.Files);
    }

    [Fact]
    public async Task OnACaseNoTeamOwns_RemovingTakesTheUnassignedWriteGrant()
    {
        var (cases, files) = Stores();
        await cases.AddCaseAsync(Unassigned(), First() with
        {
            Attachments = [new SupportAttachment { Id = "a1", FileName = "salaries.xlsx", ContentType = "application/pdf", Size = 3, Secure = true, UploadedBy = Alice }]
        });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Staffer(cases, files, SystemSupportScopes.AllManage).RemoveSecureAttachmentAsync(null, "case-unassigned", 1, "a1"));
        await Staffer(cases, files, SystemSupportScopes.Manage).RemoveSecureAttachmentAsync(null, "case-unassigned", 1, "a1");

        Assert.NotNull(SecureFileOf(await Staffer(cases, files, SystemSupportScopes.Read).GetMessagesAsync(null, "case-unassigned")).RemovedAt);
    }

    [Fact]
    public async Task ReplyingWithSecureFiles_IsAuthorizedAsAPlainReply()
    {
        var (cases, files) = Stores();
        var raised = await Member(cases, files, Alice).RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Member(cases, files, Bob).ReplyToCaseAsync(TeamA, raised.Id, "Not mine.", null, [], [Upload("x.pdf")]));

        Assert.Empty(files.Files);
    }

    private static SupportAttachment SecureFileOf(SupportMessagePage page)
        => page.Items.SelectMany(x => x.Attachments).Single(x => x.Secure);

    private static async Task<(InMemorySupportCaseStore Cases, InMemorySupportAttachmentStore Files, string CaseId, string AttachmentId)> CaseWithASecureFileAsync()
    {
        var (cases, files) = Stores();
        var alice = Member(cases, files, Alice);
        var raised = await alice.RaiseCaseAsync(TeamA, "Export", "It is empty.");
        await alice.ReplyToCaseAsync(TeamA, raised.Id, "For support only.", null, [], [Upload("salaries.xlsx")]);

        return (cases, files, raised.Id, Assert.Single(files.Files).Attachment.Id);
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

    private static ISupportCaseService Member(InMemorySupportCaseStore cases, InMemorySupportAttachmentStore files, string subject, string teamScope = null)
        => Build(cases, files, subject, memberOfTeam: TeamA, teamScopes: teamScope == null ? [] : [teamScope]);

    private static ISupportCaseService Staffer(InMemorySupportCaseStore cases, InMemorySupportAttachmentStore files, string systemScope)
        => Build(cases, files, Staff, systemScopes: [systemScope]);

    private static ISupportCaseService Build(ISupportCaseStore cases, InMemorySupportAttachmentStore files, string subject, string memberOfTeam = null, string[] teamScopes = null, string[] systemScopes = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject), new(ClaimTypes.Name, subject) };
        if (memberOfTeam != null) claims.Add(new Claim(TeamClaimTypes.TeamKey, memberOfTeam));
        claims.AddRange((teamScopes ?? []).Select(s => new Claim(TeamClaimTypes.Scope, s)));
        claims.AddRange((systemScopes ?? []).Select(s => new Claim(TeamClaimTypes.SystemScope, s)));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuthorizationSupportCaseServiceDecorator(new SupportCaseService(cases, authorizer, TimeProvider.System, attachmentStore: files), authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
