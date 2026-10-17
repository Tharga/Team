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
/// Files on support messages: listed in the transcript, opened only when the browser shows them without running
/// anything, and chosen within the limits in an answer box.
/// </summary>
public class SupportAttachmentRenderTests : BunitContext
{
    private const string ModulePath = "./_content/Tharga.Team.Blazor/support-attachments.js";
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";

    private readonly Mock<ISupportCaseService> _cases = new();

    private static SupportAttachment File(string id, string name, string contentType, long size = 2048) => new()
    {
        Id = id,
        FileName = name,
        ContentType = contentType,
        Size = size
    };

    private static SupportMessage MessageWith(params SupportAttachment[] attachments) => new()
    {
        Sequence = 3,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Body = "See the attached.",
        SentAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        Attachments = attachments
    };

    [Fact]
    public void EachFileOfAMessage_IsListedWithItsSize()
    {
        var view = RenderTranscript(MessageWith(File("a1", "report.pdf", "application/pdf", 1536), File("a2", "page.html", "text/html")));

        var listed = view.FindAll("[data-attachment]");

        Assert.Equal(["a1", "a2"], listed.Select(x => x.GetAttribute("data-attachment")).ToArray());
        Assert.Contains("report.pdf", listed[0].TextContent);
        Assert.Contains("1.5 KB", listed[0].TextContent.Replace(',', '.'));
    }

    [Fact]
    public void AMessageWithoutFiles_ListsNothing()
    {
        var view = RenderTranscript(MessageWith());

        Assert.Empty(view.FindAll("[data-attachments]"));
    }

    [Theory]
    [InlineData("application/pdf", "browser")]
    [InlineData("image/png", "browser")]
    [InlineData("image/jpeg", "browser")]
    [InlineData("image/gif", "browser")]
    [InlineData("image/webp", "browser")]
    [InlineData("text/plain; charset=utf-8", "browser")]
    [InlineData("TEXT/PLAIN", "browser")]
    [InlineData("text/html", "save")]
    [InlineData("image/svg+xml", "save")]
    [InlineData("application/xhtml+xml", "save")]
    [InlineData("application/javascript", "save")]
    [InlineData("message/rfc822", "save")]
    [InlineData("application/octet-stream", "save")]
    public void OnlyASafeType_IsOfferedToOpen(string contentType, string expected)
    {
        var view = RenderTranscript(MessageWith(File("a1", "file", contentType)));

        Assert.Equal(expected, view.Find("[data-attachment]").GetAttribute("data-opens"));
    }

    [Fact]
    public async Task APdf_OpensInTheBrowser_ThroughTheServiceForThatEntry()
    {
        var module = SetUpModule();
        var attachment = File("a1", "report.pdf", "application/pdf");
        OpensTo(attachment, 3);

        var view = RenderTranscript(MessageWith(attachment));
        await view.Find("[data-attachment='a1']").ClickAsync(new());

        _cases.Verify(x => x.OpenAttachmentAsync(TeamKey, CaseId, 3, "a1", It.IsAny<CancellationToken>()), Times.Once);
        var opened = Assert.Single(module.Invocations, x => x.Identifier == "open");
        Assert.Equal("application/pdf", opened.Arguments[1]);
        Assert.DoesNotContain(module.Invocations, x => x.Identifier == "save");
    }

    /// <summary>A page opened from the application would run with the reader's session, so it is only ever saved.</summary>
    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    public async Task AScriptCapableFile_IsSaved_NeverOpened(string contentType)
    {
        var module = SetUpModule();
        var attachment = File("a1", "page.html", contentType);
        OpensTo(attachment, 3);

        var view = RenderTranscript(MessageWith(attachment));
        await view.Find("[data-attachment='a1']").ClickAsync(new());

        var saved = Assert.Single(module.Invocations, x => x.Identifier == "save");
        Assert.Equal("page.html", saved.Arguments[1]);
        Assert.DoesNotContain(module.Invocations, x => x.Identifier == "open");
    }

    [Fact]
    public async Task AFileTheServiceCannotOpen_SaysSo()
    {
        SetUpModule();
        var view = RenderTranscript(MessageWith(File("a1", "gone.pdf", "application/pdf")));

        await view.Find("[data-attachment='a1']").ClickAsync(new());

        Assert.Contains(SupportAttachmentText.NotAvailable.Default.Replace("{0}", "gone.pdf"), view.Find("[data-attachment-unavailable]").TextContent);
    }

    [Fact]
    public void WithoutACase_FilesAreListedButCannotBeOpened()
    {
        var view = RenderTranscript(MessageWith(File("a1", "report.pdf", "application/pdf")), caseId: null);

        Assert.True(view.Find("[data-attachment='a1']").HasAttribute("disabled"));
    }

    [Fact]
    public void ThePicker_OffersToAttachFiles()
    {
        var view = RenderPicker([]);

        Assert.Contains(SupportAttachmentText.Attach.Default, view.Markup);
        Assert.Single(view.FindComponents<InputFile>());
    }

    [Fact]
    public void ChosenFiles_AreHandedToTheAnswerBox_WithTheirContent()
    {
        IReadOnlyList<SupportAttachmentUpload> chosen = null;
        var view = RenderPicker([], files => chosen = files);

        view.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("hello", "notes.txt", contentType: "text/plain"),
            InputFileContent.CreateFromBinary([1, 2, 3], "scan.pdf", contentType: "application/pdf"));

        Assert.Equal(["notes.txt", "scan.pdf"], chosen.Select(x => x.FileName).ToArray());
        Assert.Equal("application/pdf", chosen[1].ContentType);
        Assert.Equal(3, chosen[1].Content.Length);
        Assert.Empty(view.FindAll("[data-attachment-problem]"));
    }

    [Fact]
    public void AFileOverTheLimit_IsNotAdded_AndTheReaderIsToldWhy()
    {
        IReadOnlyList<SupportAttachmentUpload> chosen = null;
        var view = RenderPicker([], files => chosen = files, Limits(maxFileBytes: 4));

        view.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2, 3, 4, 5], "big.bin"),
            InputFileContent.CreateFromBinary([1], "small.bin"));

        Assert.Equal(["small.bin"], chosen.Select(x => x.FileName).ToArray());
        Assert.Contains("big.bin", view.Find("[data-attachment-problem]").TextContent);
    }

    [Fact]
    public void MoreFilesThanAllowed_AreNotAdded_AndTheReaderIsToldTheMost()
    {
        IReadOnlyList<SupportAttachmentUpload> chosen = null;
        var view = RenderPicker([], files => chosen = files, Limits(maxCount: 1));

        view.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1], "one.bin"),
            InputFileContent.CreateFromBinary([2], "two.bin"));

        Assert.Equal(["one.bin"], chosen.Select(x => x.FileName).ToArray());
        Assert.Contains(SupportAttachmentText.TooMany.Default.Replace("{0}", "1"), view.Find("[data-attachment-problem]").TextContent);
    }

    [Fact]
    public void FilesTogetherOverTheLimit_StopAtTheTotal()
    {
        IReadOnlyList<SupportAttachmentUpload> chosen = null;
        var view = RenderPicker([], files => chosen = files, Limits(maxTotalBytes: 3));

        view.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromBinary([1, 2], "first.bin"),
            InputFileContent.CreateFromBinary([3, 4], "second.bin"));

        Assert.Equal(["first.bin"], chosen.Select(x => x.FileName).ToArray());
        Assert.Contains("second.bin", view.Find("[data-attachment-problem]").TextContent);
    }

    [Fact]
    public void AChosenFile_CanBeRemoved()
    {
        IReadOnlyList<SupportAttachmentUpload> chosen = null;
        var first = Upload("first.bin");
        var second = Upload("second.bin");
        var view = RenderPicker([first, second], files => chosen = files);

        view.Find("[data-remove='first.bin']").Click();

        Assert.Same(second, Assert.Single(chosen));
    }

    [Fact]
    public void Rewound_ReadsEachFileFromItsStartAgain()
    {
        var file = Upload("again.bin");
        file.Content.Position = file.Content.Length;

        var files = SupportAttachmentPicker.Rewound([file]);

        Assert.Equal(0, Assert.Single(files).Content.Position);
    }

    [Fact]
    public void AnAnswerWithFiles_GoesThroughTheOverloadThatCarriesThem()
    {
        var supportCase = new SupportCase
        {
            Id = CaseId,
            TeamKey = null,
            AuthorIdentity = null,
            AuthorName = "stranger@example.com",
            Subject = "Help",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1
        };
        _cases.Setup(x => x.GetMessagesAsync(null, CaseId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportMessagePage { Items = [] });
        Setup();

        var view = Render<SupportUnassignedConversation>(p => p.Add(x => x.Case, supportCase));
        view.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2], "log.zip", contentType: "application/zip"));
        view.Find($"[data-answer='{CaseId}']").Click();

        _cases.Verify(x => x.AnswerCaseAsync(null, CaseId, string.Empty,
            It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 1 && f[0].FileName == "log.zip"),
            It.IsAny<CancellationToken>()), Times.Once);
        _cases.Verify(x => x.AnswerCaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReplyOrStaffAnswerWithFiles_CarriesThem_InTheCaseView(bool asStaff)
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
            MessageCount = 1
        };
        _cases.Setup(x => x.GetMessagesAsync(TeamKey, CaseId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupportMessagePage { Items = [] });
        Setup();

        var view = Render<SupportCaseConversation>(p => p.Add(x => x.Case, supportCase).Add(x => x.AsStaff, asStaff));
        view.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1], "screenshot.png", contentType: "image/png"));

        var label = asStaff ? SupportAcrossTeamsViewText.AnswerLabel.Default : SupportCasesViewText.ReplyLabel.Default;
        view.FindAll("button").First(x => x.TextContent.Contains(label) && !x.HasAttribute("disabled")).Click();

        if (asStaff)
            _cases.Verify(x => x.AnswerCaseAsync(TeamKey, CaseId, string.Empty,
                It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 1 && f[0].FileName == "screenshot.png"), It.IsAny<CancellationToken>()), Times.Once);
        else
            _cases.Verify(x => x.ReplyToCaseAsync(TeamKey, CaseId, string.Empty,
                It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 1 && f[0].FileName == "screenshot.png"), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static SupportAttachmentUpload Upload(string name)
        => new() { FileName = name, ContentType = "application/octet-stream", Content = new MemoryStream([1, 2, 3]) };

    private static SupportAttachmentLimits Limits(long maxFileBytes = 1000, long maxTotalBytes = 1000, int maxCount = 10)
        => new() { MaxFileBytes = maxFileBytes, MaxTotalBytes = maxTotalBytes, MaxCount = maxCount };

    private void OpensTo(SupportAttachment attachment, int sequence)
        => _cases.Setup(x => x.OpenAttachmentAsync(TeamKey, CaseId, sequence, attachment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SupportAttachmentContent { Attachment = attachment, Content = new MemoryStream([1, 2, 3]) });

    private BunitJSModuleInterop SetUpModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("open", _ => true).SetResult(true);
        module.SetupVoid("save", _ => true).SetVoidResult();
        return module;
    }

    private void Setup()
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        Services.AddSingleton(_cases.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<SupportTranscript> RenderTranscript(SupportMessage message, string caseId = CaseId)
    {
        Setup();

        return Render<SupportTranscript>(p => p
            .Add(x => x.Messages, [message])
            .Add(x => x.CaseAuthorIdentity, "alice")
            .Add(x => x.TeamKey, TeamKey)
            .Add(x => x.CaseId, caseId));
    }

    private IRenderedComponent<SupportAttachmentPicker> RenderPicker(IReadOnlyList<SupportAttachmentUpload> files, Action<IReadOnlyList<SupportAttachmentUpload>> changed = null, SupportAttachmentLimits limits = null)
    {
        _cases.Setup(x => x.GetAttachmentLimitsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(limits ?? SupportAttachmentLimits.Default);
        Setup();

        return Render<SupportAttachmentPicker>(p => p
            .Add(x => x.Files, files)
            .Add(x => x.FilesChanged, changed ?? (_ => { })));
    }
}
