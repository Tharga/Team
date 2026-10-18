using System.Globalization;
using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Blazor;
using Tharga.Team.Blazor.Features.Mailbox;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The postmaster's view of mail no team owns: each mail a card that reveals only who, what and when until it is
/// opened, the message read as a conversation bubble with its files, and routing, archiving and restoring inside it.
/// </summary>
public class PostmasterViewRenderTests : BunitContext
{
    private const string ModulePath = "./_content/Tharga.Team.Blazor/support-attachments.js";
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Written = Now.AddMinutes(-5);
    private static readonly DateTime ArchivedAt = Now.AddHours(2);

    private static readonly PostmasterMailSummary Held = new() { Id = "mail-1", From = "anna@example.org", Subject = "Where does this go?", ReceivedAt = Now, Reason = MailRouteReason.RetiredKey };

    private static readonly PostmasterMailSummary Archived = new() { Id = "mail-2", From = "offers@example.com", Subject = "Cheap pills", ReceivedAt = Now.AddDays(-1), Reason = MailRouteReason.UnknownAddress, ArchivedAt = ArchivedAt, ArchivedBy = "Sam Staff" };

    private static readonly InboundMailItem Opened = new()
    {
        Id = "mail-1",
        Envelope = new MailEnvelope
        {
            MessageId = "<m1@example.org>",
            From = "anna@example.org",
            FromName = "Anna",
            To = ["oldkey-mail@example.net"],
            Cc = ["bo@example.org"],
            DeliveredTo = "oldkey-mail@example.net",
            Subject = "Where does this go?",
            SentAt = Written
        },
        Reason = MailRouteReason.RetiredKey,
        ReceivedAt = Now
    };

    private static readonly ReceivedMailBody PlainBody = new() { MailId = "mail-1", Text = "Hello,\n\nis this the right address?\nAnna" };

    private static readonly ReceivedMailBody FormattedBody = new()
    {
        MailId = "mail-1",
        Text = "Hello, the invoice is attached.",
        Html = "<p>Hello, <b>the invoice</b> is attached.</p><p><img src=\"cid:logo@example.org\" alt=\"Logo\"><img src=\"cid:missing@example.org\"></p>",
        Attachments =
        [
            new SupportAttachment { Id = "1", FileName = "logo.png", ContentType = "image/png", Size = 3, Inline = true, ContentId = "logo@example.org" },
            new SupportAttachment { Id = "2", FileName = "invoice.pdf", ContentType = "application/pdf", Size = 2048 },
            new SupportAttachment { Id = "3", FileName = "tool.exe", ContentType = "application/octet-stream", Size = 10 }
        ],
        Images = [new ReceivedMailImage { ContentId = "logo@example.org", ContentType = "image/png", Content = [1, 2, 3] }]
    };

    private readonly Mock<IPostmasterService> _postmaster = new();
    private readonly Mock<ITeamMailNotifier> _notifier = new();

    public PostmasterViewRenderTests()
    {
        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Held, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [Held] });
        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Archived, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [Archived] });
        _postmaster.Setup(x => x.GetCountAsync(default)).ReturnsAsync(1);
        _postmaster.Setup(x => x.OpenAsync("mail-1", default)).ReturnsAsync(Opened);
        _postmaster.Setup(x => x.GetBodyAsync("mail-1", default)).ReturnsAsync(PlainBody);
        _postmaster.Setup(x => x.OpenAsync("mail-2", default)).ReturnsAsync(Opened with { Id = "mail-2", Outcome = PostmasterMailOutcome.Archived, HandledAt = ArchivedAt, HandledBy = "Sam Staff" });
        _postmaster.Setup(x => x.GetBodyAsync("mail-2", default)).ReturnsAsync(PlainBody with { MailId = "mail-2" });
        _postmaster.Setup(x => x.RouteAsync("mail-1", "acme", default)).ReturnsAsync(Opened with { TeamKey = "acme", Reason = MailRouteReason.Rerouted });
        _postmaster.Setup(x => x.ArchiveAsync("mail-1", default)).ReturnsAsync(Opened with { Outcome = PostmasterMailOutcome.Archived });
        _postmaster.Setup(x => x.RestoreAsync("mail-2", default)).ReturnsAsync(Opened with { Id = "mail-2" });
    }

    [Fact]
    public void EachMail_IsACard_ClosedUntilOpened_ShowingWhoWhatWhenAndWhy()
    {
        var view = RenderView();

        var card = Assert.Single(view.FindComponents<ExpandableCard>());
        Assert.Equal("Where does this go?", card.Instance.Text);
        Assert.Equal("mail", card.Instance.Icon);
        Assert.Contains("anna@example.org", view.Find("[data-mail='mail-1'] [data-sender='mail-1']").TextContent);
        Assert.Equal("Sent to a retired address", view.Find("[data-reason='mail-1']").TextContent);
        Assert.Contains("Opening a mail is recorded", view.Markup);
        Assert.Empty(view.FindAll("[data-opened]"));
        _postmaster.Verify(x => x.OpenAsync(It.IsAny<string>(), default), Times.Never);
        _postmaster.Verify(x => x.GetBodyAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public void AMailWithNoSubject_SaysSo()
    {
        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Held, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [Held with { Subject = " " }] });

        var view = RenderView();

        Assert.Equal("(no subject)", view.FindComponent<ExpandableCard>().Instance.Text);
    }

    [Fact]
    public void ByDefault_TheViewNamesItself_AndCountsTheHeldMail()
    {
        var view = RenderView();

        Assert.Contains("Postmaster", view.Find("[data-title]").TextContent);
        Assert.Equal("1 unrouted", view.Find("[data-count]").TextContent);
    }

    /// <summary>A host that puts the view in a tab shows the count in the tab's header, so the view must not say it twice.</summary>
    [Fact]
    public void WithoutItsTitle_ThereIsNoHeadingAndNoCount()
    {
        var view = RenderView(showTitle: false);

        Assert.Empty(view.FindAll("[data-title]"));
        Assert.Empty(view.FindAll("[data-count]"));
        Assert.DoesNotContain("1 unrouted", view.Markup);
        Assert.DoesNotContain(view.FindComponents<RadzenText>(), x => x.Instance.TextStyle == TextStyle.H6);
        Assert.Single(view.FindComponents<ExpandableCard>());
    }

    [Fact]
    public void OpeningTheCard_IsTheAuditedOpen_AndShowsTheEnvelope()
    {
        var view = RenderView();

        Open(view, "mail-1");

        _postmaster.Verify(x => x.OpenAsync("mail-1", default), Times.Once);
        _postmaster.Verify(x => x.GetBodyAsync("mail-1", default), Times.Once);
        var envelope = view.Find("[data-envelope='mail-1']").TextContent;
        Assert.Contains("Anna <anna@example.org>", envelope);
        Assert.Contains("oldkey-mail@example.net", envelope);
        Assert.Contains("Delivered to", envelope);
        Assert.Contains("bo@example.org", view.Find("[data-cc='mail-1']").TextContent);
    }

    [Fact]
    public void TheMessage_IsAConversationBubble_WithTheSenderAndWhenItWasWritten()
    {
        var view = RenderView(timeTemplate: HostTime);

        Open(view, "mail-1");

        var bubble = view.Find("[data-bubble='mail-1']");
        Assert.Contains("Anna", bubble.TextContent);
        Assert.Equal(HostTimeText(Written), view.Find("[data-written='mail-1'] .host-time").TextContent);
        Assert.Contains("background", bubble.GetAttribute("style"));
        Assert.Single(view.FindAll("[data-side='left'] [data-sender='author']"));
    }

    [Fact]
    public void PlainText_IsShownAsParagraphs()
    {
        var view = RenderView();

        Open(view, "mail-1");

        var paragraphs = view.FindAll("[data-body-text='mail-1'] > *");
        Assert.Equal(2, paragraphs.Count);
        Assert.Equal("Hello,", paragraphs[0].TextContent);
        Assert.Equal("is this the right address?\nAnna", paragraphs[1].TextContent);
        Assert.Empty(view.FindAll("[data-body-html]"));
    }

    [Fact]
    public void Formatting_IsShownThroughTheSanitizer_WithItsImageInPlace_AndAnImageItDoesNotCarryAsText()
    {
        _postmaster.Setup(x => x.GetBodyAsync("mail-1", default)).ReturnsAsync(FormattedBody);
        var view = RenderView();

        Open(view, "mail-1");

        var html = view.Find("[data-body-html='mail-1']").InnerHtml;
        Assert.Contains("<b>the invoice</b>", html);
        Assert.Contains("src=\"data:image/png;base64,AQID\"", html);
        Assert.DoesNotContain("cid:", html);
        Assert.Empty(view.FindAll("[data-body-text]"));
    }

    [Fact]
    public void TheFiles_AreListedByNameAndSize_AndAnImageShownInPlaceIsNotListedAgain()
    {
        _postmaster.Setup(x => x.GetBodyAsync("mail-1", default)).ReturnsAsync(FormattedBody);
        var view = RenderView();

        Open(view, "mail-1");

        var files = view.FindAll("[data-attachments='mail-1'] [data-attachment]");
        Assert.Equal(["2", "3"], files.Select(x => x.GetAttribute("data-attachment")));
        Assert.Contains("invoice.pdf (2 KB)", files[0].TextContent);
        Assert.Equal("browser", files[0].GetAttribute("data-opens"));
        Assert.Equal("save", files[1].GetAttribute("data-opens"));
    }

    [Fact]
    public async Task AFile_IsOpenedThroughTheService()
    {
        _postmaster.Setup(x => x.GetBodyAsync("mail-1", default)).ReturnsAsync(FormattedBody);
        _postmaster.Setup(x => x.OpenAttachmentAsync("mail-1", "2", default))
            .ReturnsAsync(() => new SupportAttachmentContent { Attachment = FormattedBody.Attachments[1], Content = new MemoryStream([9, 9]) });
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("open", _ => true).SetResult(true);
        var view = RenderView();
        Open(view, "mail-1");

        await view.InvokeAsync(() => view.Find("[data-attachment='2']").Click());

        _postmaster.Verify(x => x.OpenAttachmentAsync("mail-1", "2", default), Times.Once);
        Assert.Single(module.Invocations, x => x.Identifier == "open");
    }

    [Fact]
    public async Task AFileNoLongerKept_SaysSo()
    {
        _postmaster.Setup(x => x.GetBodyAsync("mail-1", default)).ReturnsAsync(FormattedBody);
        _postmaster.Setup(x => x.OpenAttachmentAsync("mail-1", "3", default)).ReturnsAsync((SupportAttachmentContent)null);
        var view = RenderView();
        Open(view, "mail-1");

        await view.InvokeAsync(() => view.Find("[data-attachment='3']").Click());

        Assert.Contains("tool.exe", view.Find("[data-attachment-unavailable='3']").TextContent);
    }

    [Fact]
    public void AMailWithNoText_SaysSo()
    {
        _postmaster.Setup(x => x.GetBodyAsync("mail-1", default)).ReturnsAsync(new ReceivedMailBody { MailId = "mail-1", Text = string.Empty });
        var view = RenderView();

        Open(view, "mail-1");

        Assert.Equal("The mail has no text.", view.Find("[data-no-text='mail-1']").TextContent);
    }

    [Fact]
    public void AMailWhoseContentIsNoLongerKept_SaysSo_AndStillShowsItsEnvelope()
    {
        _postmaster.Setup(x => x.GetBodyAsync("mail-1", default)).ReturnsAsync((ReceivedMailBody)null);
        var view = RenderView();

        Open(view, "mail-1");

        Assert.Equal("What the mail said is no longer kept.", view.Find("[data-content-unavailable='mail-1']").TextContent);
        Assert.Single(view.FindAll("[data-envelope='mail-1']"));
    }

    [Fact]
    public void AMailGoneBeforeItWasOpened_SaysSo()
    {
        _postmaster.Setup(x => x.OpenAsync("mail-1", default)).ReturnsAsync((InboundMailItem)null);
        var view = RenderView();

        Open(view, "mail-1");

        Assert.Equal("The postmaster no longer holds this mail.", view.Find("[data-gone='mail-1']").TextContent);
        _postmaster.Verify(x => x.GetBodyAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task AHeldMail_IsRoutedToTheTeamTheReaderNames()
    {
        var view = RenderView();
        Open(view, "mail-1");

        await view.InvokeAsync(() => view.FindComponent<RadzenTextBox>().Instance.ValueChanged.InvokeAsync("acme"));
        await view.InvokeAsync(() => view.Find("[data-route='mail-1']").Click());

        _postmaster.Verify(x => x.RouteAsync("mail-1", "acme", default), Times.Once);
        Assert.Contains(Notifications(), x => x.Summary == "The mail was delivered to the team.");
    }

    [Fact]
    public async Task AKeyNoTeamHas_IsSaidSo_AndTheMailStays()
    {
        _postmaster.Setup(x => x.RouteAsync("mail-1", "nobody", default)).ThrowsAsync(new ArgumentException("There is no team 'nobody'."));
        var view = RenderView();
        Open(view, "mail-1");

        await view.InvokeAsync(() => view.FindComponent<RadzenTextBox>().Instance.ValueChanged.InvokeAsync("nobody"));
        await view.InvokeAsync(() => view.Find("[data-route='mail-1']").Click());

        Assert.Contains(Notifications(), x => x.Summary == "No team has that key.");
        Assert.Single(view.FindAll("[data-mail='mail-1']"));
    }

    [Fact]
    public async Task Archiving_AsksFirst_AndOnlyThenArchives()
    {
        var view = RenderView();
        var dialogs = Render<RadzenDialog>();
        Open(view, "mail-1");

        var asking = view.InvokeAsync(() => view.Find("[data-archive='mail-1']").Click());

        dialogs.WaitForAssertion(() => Assert.Contains("The mail goes to no team.", dialogs.Markup));
        _postmaster.Verify(x => x.ArchiveAsync(It.IsAny<string>(), default), Times.Never);

        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Held, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [] });
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Last(b => b.TextContent.Contains("Archive")).Click());
        await asking;

        _postmaster.Verify(x => x.ArchiveAsync("mail-1", default), Times.Once);
        Assert.Contains(Notifications(), x => x.Summary == "The mail was archived.");
        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-mail]")));
        Assert.Equal("No mail is waiting for a team.", view.Find("[data-empty]").TextContent.Trim());
    }

    [Fact]
    public async Task ArchivingCancelled_ArchivesNothing()
    {
        var view = RenderView();
        var dialogs = Render<RadzenDialog>();
        Open(view, "mail-1");

        var asking = view.InvokeAsync(() => view.Find("[data-archive='mail-1']").Click());
        dialogs.WaitForAssertion(() => Assert.Contains("Archive this mail?", dialogs.Markup));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Last(b => b.TextContent.Contains("Cancel")).Click());
        await asking;

        _postmaster.Verify(x => x.ArchiveAsync(It.IsAny<string>(), default), Times.Never);
        Assert.Single(view.FindAll("[data-mail='mail-1']"));
    }

    [Fact]
    public async Task AReaderWhoMayNotDecide_IsToldSo_RatherThanLosingThePage()
    {
        _postmaster.Setup(x => x.ArchiveAsync("mail-1", default)).ThrowsAsync(new UnauthorizedAccessException());
        var view = RenderView();
        var dialogs = Render<RadzenDialog>();
        Open(view, "mail-1");

        var asking = view.InvokeAsync(() => view.Find("[data-archive='mail-1']").Click());
        dialogs.WaitForAssertion(() => Assert.Contains("Archive this mail?", dialogs.Markup));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Last(b => b.TextContent.Contains("Archive")).Click());
        await asking;

        Assert.Contains(Notifications(), x => x.Summary == "You may read this mail, but not route, archive or restore it.");
        Assert.Single(view.FindAll("[data-opened='mail-1']"));
    }

    [Fact]
    public async Task TheSwitch_ShowsTheArchivedMail_WithWhenAndByWhom()
    {
        var view = RenderView(timeTemplate: HostTime);

        await ChooseAsync(view, PostmasterMailState.Archived);

        Assert.Equal(["mail-2"], MailIds(view));
        Assert.Equal("archive", view.FindComponent<ExpandableCard>().Instance.Icon);
        var archived = view.Find("[data-archived='mail-2']");
        Assert.Contains("Archived by Sam Staff", archived.TextContent);
        Assert.Equal(HostTimeText(ArchivedAt), archived.QuerySelector(".host-time")!.TextContent);
        _postmaster.Verify(x => x.ListAsync(PostmasterMailState.Archived, null, 50, default), Times.Once);
    }

    [Fact]
    public async Task AnArchivedMailWithNoName_SaysOnlyWhen()
    {
        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Archived, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [Archived with { ArchivedBy = null }] });
        var view = RenderView();

        await ChooseAsync(view, PostmasterMailState.Archived);

        var archived = view.Find("[data-archived='mail-2']").TextContent;
        Assert.Contains("Archived", archived);
        Assert.DoesNotContain("Archived by", archived);
    }

    [Fact]
    public async Task NoArchivedMail_SaysSo()
    {
        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Archived, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [] });
        var view = RenderView();

        await ChooseAsync(view, PostmasterMailState.Archived);

        Assert.Equal("No mail has been archived.", view.Find("[data-empty]").TextContent.Trim());
    }

    [Fact]
    public async Task AnArchivedMail_IsRestored_AndOffersNeitherRoutingNorArchiving()
    {
        var view = RenderView();
        await ChooseAsync(view, PostmasterMailState.Archived);
        Open(view, "mail-2");

        Assert.Empty(view.FindAll("[data-archive]"));
        Assert.Empty(view.FindAll("[data-route]"));

        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Archived, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [] });
        await view.InvokeAsync(() => view.Find("[data-restore='mail-2']").Click());

        _postmaster.Verify(x => x.RestoreAsync("mail-2", default), Times.Once);
        Assert.Contains(Notifications(), x => x.Summary == "The mail is back among the held mail.");
        Assert.Empty(MailIds(view));
    }

    [Fact]
    public void AHeldMail_OffersRoutingAndArchiving_NotRestoring()
    {
        var view = RenderView();

        Open(view, "mail-1");

        Assert.Single(view.FindAll("[data-route='mail-1']"));
        Assert.Single(view.FindAll("[data-archive='mail-1']"));
        Assert.Empty(view.FindAll("[data-restore]"));
    }

    [Fact]
    public void EveryDate_GoesThroughTheHostsTimeTemplate()
    {
        var view = RenderView(timeTemplate: HostTime);

        Assert.Equal(HostTimeText(Now), view.Find("[data-time='mail-1'] .host-time").TextContent);

        Open(view, "mail-1");

        Assert.Contains(HostTimeText(Now), view.Find("[data-envelope='mail-1']").TextContent);
        Assert.DoesNotContain(Now.ToLocalTime().ToString("g"), view.Markup);
    }

    /// <summary>A change made on another instance reaches no in-process notifier, so the host asks for the re-read.</summary>
    [Fact]
    public async Task RefreshingReReadsTheListAndTheCount()
    {
        var view = RenderView();
        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Held, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [Held, Held with { Id = "mail-3", Subject = "Arrived elsewhere" }] });
        _postmaster.Setup(x => x.GetCountAsync(default)).ReturnsAsync(2);

        await view.Instance.RefreshAsync();

        Assert.Equal(["mail-1", "mail-3"], MailIds(view));
        Assert.Equal("2 unrouted", view.Find("[data-count]").TextContent);
    }

    /// <summary>Opening is recorded, so a list read again — by a refresh or a mail arriving — must not open an open card again.</summary>
    [Fact]
    public async Task AnOpenCard_IsNotOpenedAgain_WhenTheListIsReadAgain()
    {
        var view = RenderView();
        Open(view, "mail-1");

        await view.Instance.RefreshAsync();
        await view.Instance.RefreshAsync();

        _postmaster.Verify(x => x.OpenAsync("mail-1", default), Times.Once);
        _postmaster.Verify(x => x.GetBodyAsync("mail-1", default), Times.Once);
        Assert.Single(view.FindAll("[data-opened='mail-1']"));
    }

    [Theory]
    [InlineData(TeamMailChange.Archived)]
    [InlineData(TeamMailChange.Restored)]
    [InlineData(TeamMailChange.Received)]
    public void APostmasterChangeInThisProcess_ReReadsTheList(TeamMailChange change)
    {
        var view = RenderView();
        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Held, null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [] });

        _notifier.Raise(x => x.MailUpdated += null, new TeamMailUpdatedEventArgs { TeamKey = null, MailId = "mail-1", Change = change });

        view.WaitForAssertion(() => Assert.Empty(MailIds(view)));
    }

    [Fact]
    public void ATeamsOwnMailBeingHandled_DoesNotReReadTheList()
    {
        var view = RenderView();

        _notifier.Raise(x => x.MailUpdated += null, new TeamMailUpdatedEventArgs { TeamKey = "acme", MailId = "other", Change = TeamMailChange.Handled });

        _postmaster.Verify(x => x.ListAsync(PostmasterMailState.Held, null, 50, default), Times.Once);
    }

    [Fact]
    public void WithoutTheSystemGrant_ThePostmasterIsRefused_NotShown()
    {
        _postmaster.Setup(x => x.ListAsync(PostmasterMailState.Held, null, 50, default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = RenderView();

        Assert.Contains("You do not have access to mail that belongs to no team.", view.Markup);
        Assert.DoesNotContain("Where does this go?", view.Markup);
    }

    private IRenderedComponent<PostmasterView> RenderView(bool showTitle = true, RenderFragment<DateTime> timeTemplate = null)
    {
        Services.AddSingleton(_postmaster.Object);
        Services.AddSingleton(_notifier.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", true));

        return Render<PostmasterView>(p => p
            .Add(x => x.ShowTitle, showTitle)
            .Add(x => x.TimeTemplate, timeTemplate));
    }

    private IEnumerable<NotificationMessage> Notifications() => Services.GetRequiredService<NotificationService>().Messages;

    private static void Open(IRenderedComponent<PostmasterView> view, string mailId)
        => view.Find($"[data-mail='{mailId}'] [style*='cursor: pointer']").Click();

    private static Task ChooseAsync(IRenderedComponent<PostmasterView> view, PostmasterMailState state)
    {
        var bar = view.FindComponent<RadzenSelectBar<PostmasterMailState>>();
        return view.InvokeAsync(() => bar.Instance.Change.InvokeAsync(state));
    }

    private static string[] MailIds(IRenderedComponent<PostmasterView> view)
        => [.. view.FindAll("[data-mail]").Select(x => x.GetAttribute("data-mail"))];

    private static readonly RenderFragment<DateTime> HostTime = date => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "host-time");
        builder.AddContent(2, HostTimeText(date));
        builder.CloseElement();
    };

    private static string HostTimeText(DateTime date) => "host " + date.ToString("O", CultureInfo.InvariantCulture);
}
