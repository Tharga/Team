using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The domain half of secure files: kept as secure with who uploaded them, refused where they could not stay secure, and
/// never handed outward. Who may see and open one is the decorator's, and is tested there.
/// </summary>
public class SupportSecureFileServiceTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";

    private static SupportAttachmentUpload Upload(string name, int size = 3, string contentType = "application/pdf")
        => new() { FileName = name, ContentType = contentType, Content = new MemoryStream(Enumerable.Repeat((byte)1, size).ToArray()) };

    [Fact]
    public async Task AReply_KeepsItsSecureFiles_AsSecure_WithWhoUploadedThem()
    {
        var (cases, files) = Stores();
        var service = Service(cases, files);
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await service.ReplyToCaseAsync(TeamA, raised.Id, "Both attached.", null, [Upload("public.pdf")], [Upload("salaries.xlsx")]);

        var reply = (await cases.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items[^1];
        Assert.Equal(2, reply.Attachments.Length);
        var ordinary = reply.Attachments.Single(x => x.FileName == "public.pdf");
        var secure = reply.Attachments.Single(x => x.FileName == "salaries.xlsx");
        Assert.False(ordinary.Secure);
        Assert.Null(ordinary.UploadedBy);
        Assert.True(secure.Secure);
        Assert.Equal(Alice, secure.UploadedBy);
        Assert.Null(secure.RemovedAt);
        Assert.All(files.Files, x => Assert.Equal(raised.Id, x.CaseId));
    }

    [Fact]
    public async Task RaisingACase_WithSecureFiles_CarriesThemOnTheFirstEntry()
    {
        var (cases, files) = Stores();

        var raised = await Service(cases, files).RaiseCaseAsync(TeamA, "Export", "See the file.", new SupportRaiseOptions { SecureAttachments = [Upload("export.csv", 4, "text/csv")] });

        var first = Assert.Single((await cases.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items);
        var secure = Assert.Single(first.Attachments);
        Assert.True(secure.Secure);
        Assert.Equal(Alice, secure.UploadedBy);
        Assert.Equal(raised.Id, Assert.Single(files.Files).CaseId);
    }

    [Fact]
    public async Task SecureAndOrdinaryFiles_CountTogether_AgainstTheLimits()
    {
        var (cases, files) = Stores();
        var service = Service(cases, files, new SupportAttachmentLimits { MaxFileBytes = 10, MaxTotalBytes = 15, MaxCount = 2 });
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplyToCaseAsync(TeamA, raised.Id, "Three.", null, [Upload("a.pdf")], [Upload("b.pdf"), Upload("c.pdf")]));

        Assert.Empty(files.Files);
        Assert.Single((await cases.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items);
    }

    /// <summary>A store that does not say it keeps them secure would keep the file and drop the flag, so nothing is written to it.</summary>
    [Fact]
    public async Task AStoreThatDoesNotKeepThemSecure_IsRefused_BeforeAnythingIsWritten()
    {
        var (cases, files) = Stores();
        cases.KeepsSecureAttachments = false;
        var service = Service(cases, files);
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplyToCaseAsync(TeamA, raised.Id, "Secret.", null, [], [Upload("salaries.xlsx")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RaiseCaseAsync(TeamA, "Again", "Secret.", new SupportRaiseOptions { SecureAttachments = [Upload("salaries.xlsx")] }));

        Assert.Empty(files.Files);
        Assert.Single((await cases.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items);
        Assert.Single((await cases.GetCasesAsync(TeamA, null, 50)).Items);
    }

    /// <summary>Fail closed: a store that says it keeps them and drops the flag must not turn a secure file into one every reader opens.</summary>
    [Fact]
    public async Task AStoreThatDropsTheSecureFlag_HasTheContentRemoved_AndTheUploadIsRefused()
    {
        var (cases, files) = Stores();
        cases.DropsSecureFlag = true;
        var service = Service(cases, files);
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplyToCaseAsync(TeamA, raised.Id, "Secret.", null, [Upload("public.pdf")], [Upload("salaries.xlsx")]));

        var kept = Assert.Single(files.Files);
        Assert.Equal("public.pdf", kept.Attachment.FileName);
        Assert.Single(files.Deleted);
    }

    [Fact]
    public async Task WithNoAttachmentStore_SecureFilesAreRefused()
    {
        var cases = new InMemorySupportCaseStore();
        var service = Service(cases, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RaiseCaseAsync(TeamA, "Export", "Secret.", new SupportRaiseOptions { SecureAttachments = [Upload("salaries.xlsx")] }));

        Assert.Empty((await cases.GetCasesAsync(TeamA, null, 50)).Items);
    }

    [Fact]
    public async Task ACaller_WithNoSubject_CannotUploadASecureFile()
    {
        var (cases, files) = Stores();
        var service = Service(cases, files, subject: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RaiseCaseAsync(TeamA, "Export", "Secret.", new SupportRaiseOptions { SecureAttachments = [Upload("salaries.xlsx")] }));

        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task AChannel_IsGivenTheReply_WithoutItsSecureFiles()
    {
        var (cases, files) = Stores();
        var channel = new RecordingChannel();
        var service = Service(cases, files, channels: [channel]);
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "It is empty.");

        await service.ReplyToCaseAsync(TeamA, raised.Id, "Both attached.", null, [Upload("public.pdf")], [Upload("salaries.xlsx")]);

        var posted = Assert.Single(channel.Posted);
        Assert.Equal("public.pdf", Assert.Single(posted.Attachments).FileName);
        Assert.Equal(SupportMessageDelivery.Sent, (await cases.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items[^1].Delivery);
    }

    [Fact]
    public async Task SendingAgain_LeavesTheSecureFilesBehindToo()
    {
        var (cases, files) = Stores();
        var channel = new RecordingChannel { Accepts = false };
        var service = Service(cases, files, channels: [channel]);
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "It is empty.");
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Secret.", null, [], [Upload("salaries.xlsx")]);

        channel.Accepts = true;
        await service.ResendAsync(TeamA, raised.Id, 2);

        Assert.All(channel.Posted, x => Assert.Empty(x.Attachments));
        Assert.Equal(2, channel.Posted.Count);
    }

    [Fact]
    public async Task TheAssistant_HearsNothingOfASecureFile()
    {
        var (cases, files) = Stores();
        var responder = new RecordingResponder();
        var service = Service(cases, files, responder: responder);
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "See the file.", new SupportRaiseOptions { Assistance = SupportAssistance.Assistant, SecureAttachments = [Upload("salaries.xlsx")] });

        await service.RunAssistantAsync(TeamA, raised.Id);

        var heard = Assert.Single(responder.Transcripts);
        Assert.Empty(Assert.Single(heard).Attachments);
        Assert.True(Assert.Single((await cases.GetMessagesAsync(TeamA, raised.Id, null, 50)).Items[0].Attachments).Secure);
    }

    [Fact]
    public async Task RemovingASecureFile_RemovesItsContent_AndTheEntrySaysSo()
    {
        var (cases, files, service, caseId, attachmentId) = await CaseWithASecureFileAsync();

        await service.RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId);

        Assert.Empty(files.Files);
        var removed = Assert.Single((await cases.GetMessagesAsync(TeamA, caseId, null, 50)).Items[^1].Attachments);
        Assert.NotNull(removed.RemovedAt);
        Assert.Equal("salaries.xlsx", removed.FileName);
        Assert.Null(await service.OpenAttachmentAsync(TeamA, caseId, 2, attachmentId));
    }

    [Fact]
    public async Task RemovingItAgain_IsNotAnError_AndRecordsNothingNew()
    {
        var (cases, _, service, caseId, attachmentId) = await CaseWithASecureFileAsync();

        await service.RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId);
        await service.RemoveSecureAttachmentAsync(TeamA, caseId, 2, attachmentId);

        Assert.Equal(1, cases.MarkedRemoved);
    }

    [Fact]
    public async Task AnOrdinaryFile_CannotBeRemovedAsASecureOne()
    {
        var (cases, files) = Stores();
        var service = Service(cases, files);
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "It is empty.");
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Public.", [Upload("public.pdf")]);
        var id = Assert.Single(files.Files).Attachment.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveSecureAttachmentAsync(TeamA, raised.Id, 2, id));

        Assert.Single(files.Files);
    }

    [Fact]
    public async Task TheLimits_SayWhetherSecureFilesAreTaken_AndHowLongTheyAreKept()
    {
        var (cases, files) = Stores();

        var limits = await Service(cases, files, keepFor: TimeSpan.FromDays(30)).GetAttachmentLimitsAsync();
        cases.KeepsSecureAttachments = false;
        var refused = await Service(cases, files, keepFor: TimeSpan.FromDays(30)).GetAttachmentLimitsAsync();
        cases.KeepsSecureAttachments = true;
        var keptUntilRemoved = await Service(cases, files, keepFor: TimeSpan.Zero).GetAttachmentLimitsAsync();

        Assert.True(limits.AcceptsSecureFiles);
        Assert.Equal(TimeSpan.FromDays(30), limits.SecureKeepFor);
        Assert.False(refused.AcceptsSecureFiles);
        Assert.Null(refused.SecureKeepFor);
        Assert.True(keptUntilRemoved.AcceptsSecureFiles);
        Assert.Null(keptUntilRemoved.SecureKeepFor);
    }

    [Fact]
    public async Task AStoreWrittenBeforeSecureFiles_DoesNotKeepThem_FindsNoneDue_AndCannotRecordARemoval()
    {
        ISupportCaseStore store = new StoreWithoutReopen(new InMemorySupportCaseStore());

        Assert.False(store.KeepsSecureAttachments);
        Assert.Empty(await store.GetSecureAttachmentsDueAsync(DateTime.UtcNow, 10));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.TryMarkAttachmentRemovedAsync("c1", 1, "a1", DateTime.UtcNow));
    }

    private static async Task<(InMemorySupportCaseStore Cases, InMemorySupportAttachmentStore Files, ISupportCaseService Service, string CaseId, string AttachmentId)> CaseWithASecureFileAsync()
    {
        var (cases, files) = Stores();
        var service = Service(cases, files);
        var raised = await service.RaiseCaseAsync(TeamA, "Export", "It is empty.");
        await service.ReplyToCaseAsync(TeamA, raised.Id, "Secret.", null, [], [Upload("salaries.xlsx")]);

        return (cases, files, service, raised.Id, Assert.Single(files.Files).Attachment.Id);
    }

    private static (InMemorySupportCaseStore, InMemorySupportAttachmentStore) Stores() => (new InMemorySupportCaseStore(), new InMemorySupportAttachmentStore());

    private static ISupportCaseService Service(
        InMemorySupportCaseStore cases,
        InMemorySupportAttachmentStore files,
        SupportAttachmentLimits limits = null,
        string subject = Alice,
        ISupportChannel[] channels = null,
        ISupportResponder responder = null,
        TimeSpan? keepFor = null)
        => new SupportCaseService(cases, Authorizer(subject), TimeProvider.System, channels: channels, responder: responder, attachmentStore: files, attachmentLimits: limits, secureKeepFor: keepFor);

    private static TeamAuthorizer Authorizer(string subject)
    {
        var claims = new List<Claim> { new(TeamClaimTypes.TeamKey, TeamA) };
        if (subject != null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));
            claims.Add(new Claim(ClaimTypes.Name, subject));
        }

        return new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    private sealed class RecordingChannel : ISupportChannel
    {
        public bool Accepts { get; set; } = true;

        public List<SupportMessage> Posted { get; } = [];

        public SupportChannelType ChannelType => SupportChannelType.Slack;

        public Task<SupportChannelBinding> OpenAsync(SupportCase supportCase, string openingMessage, CancellationToken cancellationToken = default)
            => Task.FromResult(new SupportChannelBinding { ChannelType = SupportChannelType.Slack, ExternalId = "1.0" });

        public Task<bool> PostAsync(SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default)
        {
            Posted.Add(message);
            return Task.FromResult(Accepts);
        }
    }

    private sealed class RecordingResponder : ISupportResponder
    {
        public List<IReadOnlyList<SupportMessage>> Transcripts { get; } = [];

        public Task<SupportAnswer> AnswerAsync(SupportCase supportCase, IReadOnlyList<SupportMessage> transcript, CancellationToken cancellationToken = default)
        {
            Transcripts.Add(transcript);
            return Task.FromResult(SupportAnswer.FromBody("Reindex the export."));
        }
    }
}
