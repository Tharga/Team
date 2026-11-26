using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Files for support only in the components: the picker's secure mode, shown only where the service takes them; the transcript telling a
/// secure, a withheld and a removed file apart; a member's reply carrying them; and support removing one.
/// </summary>
public class SupportSecureFileRenderTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";

    private readonly Mock<ISupportCaseService> _cases = new();

    private static readonly SupportAttachmentLimits Accepting = SupportAttachmentLimits.Default with { AcceptsSecureFiles = true, SecureKeepFor = TimeSpan.FromDays(30) };

    private static SupportAttachment Secure(string id = "s1", bool withheld = false, DateTime? removedAt = null) => new()
    {
        Id = id,
        FileName = withheld ? string.Empty : "salaries.xlsx",
        ContentType = withheld ? string.Empty : "application/octet-stream",
        Size = 2048,
        Secure = true,
        UploadedBy = withheld ? null : "alice",
        Withheld = withheld,
        RemovedAt = removedAt
    };

    private static SupportMessage MessageWith(params SupportAttachment[] attachments) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Body = "For support only.",
        SentAt = new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc),
        Attachments = attachments
    };

    [Fact]
    public void TheSecurePicker_ShowsNothing_WhereTheServiceDoesNotTakeSecureFiles()
    {
        var view = RenderPicker(SupportAttachmentLimits.Default);

        Assert.Empty(view.FindAll("[data-attachment-picker]"));
        Assert.Empty(view.FindComponents<InputFile>());
    }

    [Fact]
    public void TheSecurePicker_SaysWhoMayOpenTheFiles_AndWhenTheyGo()
    {
        var view = RenderPicker(Accepting);

        Assert.Equal("secure", view.Find("[data-attachment-picker]").GetAttribute("data-attachment-picker"));
        Assert.Contains(SupportAttachmentText.AttachSecure.Default, view.Markup);
        Assert.Contains("removed after 30 days", view.Find("[data-secure-note]").TextContent);
    }

    [Fact]
    public void FilesKeptUntilSupportRemovesThem_AreNotSaidToGo()
    {
        var view = RenderPicker(Accepting with { SecureKeepFor = null });

        Assert.Equal(SupportAttachmentText.SecureNote.Default, view.Find("[data-secure-note]").TextContent.Trim());
    }

    [Fact]
    public void FilesChosenInTheOtherPicker_CountAgainstTheSharedLimits()
    {
        IReadOnlyList<SupportAttachmentUpload> chosen = null;
        var view = RenderPicker(Accepting with { MaxCount = 2 }, files => chosen = files, alongWith: [Upload("public.pdf")]);

        view.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1], "a.xlsx", contentType: "application/octet-stream"),
            InputFileContent.CreateFromBinary([2], "b.xlsx", contentType: "application/octet-stream"));

        Assert.Equal(["a.xlsx"], chosen.Select(x => x.FileName).ToArray());
        Assert.NotEmpty(view.FindAll("[data-attachment-problem]"));
    }

    [Fact]
    public void ASecureFile_ShownWhole_IsMarkedAndOpens()
    {
        var view = RenderTranscript(MessageWith(Secure()));

        Assert.NotNull(view.Find("[data-secure='s1']"));
        Assert.Contains("salaries.xlsx", view.Find("[data-attachment='s1']").TextContent);
        Assert.Empty(view.FindAll("[data-remove-secure]"));
    }

    [Fact]
    public void AWithheldFile_SaysOnlyThatItIsThere_AndOffersNothingToOpen()
    {
        var view = RenderTranscript(MessageWith(Secure(withheld: true)));

        var withheld = view.Find("[data-attachment-withheld='s1']");
        Assert.Contains("A file for support only", withheld.TextContent);
        Assert.Contains("2 KB", withheld.TextContent.Replace(',', '.'));
        Assert.Empty(view.FindAll("[data-attachment]"));
    }

    [Fact]
    public void AWithheldFileThatWasRemoved_SaysSo()
    {
        var view = RenderTranscript(MessageWith(Secure(withheld: true, removedAt: DateTime.UtcNow)));

        Assert.Contains(SupportAttachmentText.WithheldRemoved.Default, view.Find("[data-attachment-withheld='s1']").TextContent);
    }

    [Fact]
    public void ARemovedFile_IsNamed_AndOffersNothingToOpen()
    {
        var view = RenderTranscript(MessageWith(Secure(removedAt: DateTime.UtcNow)));

        Assert.Contains("salaries.xlsx, removed", view.Find("[data-attachment-removed='s1']").TextContent);
        Assert.Empty(view.FindAll("[data-attachment]"));
    }

    [Fact]
    public void SupportIsOfferedToRemoveASecureFile_OnlyWhenTheViewPassesHowTo()
    {
        (SupportMessage Message, SupportAttachment Attachment)? removed = null;
        var view = RenderTranscript(MessageWith(Secure(), new SupportAttachment { Id = "o1", FileName = "public.pdf", ContentType = "application/pdf", Size = 10 }),
            remove: x => removed = x);

        Assert.Single(view.FindAll("[data-remove-secure]"));
        view.Find("[data-remove-secure='s1']").Click();

        Assert.Equal("s1", removed?.Attachment.Id);
        Assert.Equal(2, removed?.Message.Sequence);
    }

    [Fact]
    public void AMembersReplyWithFilesForSupportOnly_GoesThroughTheOverloadThatCarriesThem()
    {
        var view = RenderConversation(asStaff: false);

        var pickers = view.FindComponents<SupportAttachmentPicker>();
        Assert.Equal(2, pickers.Count);
        pickers.Single(x => x.Instance.Secure).FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2], "salaries.xlsx", contentType: "application/octet-stream"));
        view.FindAll("button").First(x => x.TextContent.Contains(SupportCasesViewText.ReplyLabel.Default) && !x.HasAttribute("disabled")).Click();

        _cases.Verify(x => x.ReplyToCaseAsync(TeamKey, CaseId, string.Empty, string.Empty,
            It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 0),
            It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 1 && f[0].FileName == "salaries.xlsx"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void SupportsAnswerBox_OffersNoSecureFiles()
    {
        var view = RenderConversation(asStaff: true);

        Assert.DoesNotContain(view.FindComponents<SupportAttachmentPicker>(), x => x.Instance.Secure);
    }

    [Fact]
    public void SupportRemovesASecureFile_ThroughTheService()
    {
        var view = RenderConversation(asStaff: true, MessageWith(Secure()));

        view.Find("[data-remove-secure='s1']").Click();

        _cases.Verify(x => x.RemoveSecureAttachmentAsync(TeamKey, CaseId, 2, "s1", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SupportAttachmentUpload Upload(string name)
        => new() { FileName = name, ContentType = "application/octet-stream", Content = new MemoryStream([1, 2, 3]) };

    private void Setup(SupportAttachmentLimits limits = null)
    {
        _cases.Setup(x => x.GetAttachmentLimitsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(limits ?? Accepting);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        Services.AddSingleton(_cases.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<SupportAttachmentPicker> RenderPicker(SupportAttachmentLimits limits, Action<IReadOnlyList<SupportAttachmentUpload>> changed = null, IReadOnlyList<SupportAttachmentUpload> alongWith = null)
    {
        Setup(limits);

        return Render<SupportAttachmentPicker>(p => p
            .Add(x => x.Secure, true)
            .Add(x => x.Files, [])
            .Add(x => x.AlongWith, alongWith ?? [])
            .Add(x => x.FilesChanged, changed ?? (_ => { })));
    }

    private IRenderedComponent<SupportTranscript> RenderTranscript(SupportMessage message, Action<(SupportMessage Message, SupportAttachment Attachment)> remove = null)
    {
        Setup();

        return Render<SupportTranscript>(p =>
        {
            p.Add(x => x.Messages, [message]).Add(x => x.CaseAuthorIdentity, "alice").Add(x => x.TeamKey, TeamKey).Add(x => x.CaseId, CaseId);
            if (remove != null) p.Add(x => x.RemoveSecure, remove);
        });
    }

    private IRenderedComponent<SupportCaseConversation> RenderConversation(bool asStaff, params SupportMessage[] messages)
    {
        var supportCase = new SupportCase
        {
            Id = CaseId,
            TeamKey = TeamKey,
            AuthorIdentity = "alice",
            AuthorName = "Alice",
            Subject = "Export",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1 + messages.Length
        };
        _cases.Setup(x => x.GetMessagesAsync(TeamKey, CaseId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportMessagePage { Items = messages });
        Setup();

        return Render<SupportCaseConversation>(p => p.Add(x => x.Case, supportCase).Add(x => x.AsStaff, asStaff));
    }
}
