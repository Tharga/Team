using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Beside every support answer box: the address the answer is mailed to and the support address it goes out from, or
/// that it is read on the site; what this environment does with it when that is not what it says; and a Preview that
/// shows the mail as the service renders it.
/// </summary>
public class SupportAnswerRouteRenderTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string Customer = "user@customer.example";

    private static readonly SupportCase Case = new()
    {
        Id = "case-1",
        TeamKey = TeamKey,
        AuthorIdentity = null,
        AuthorName = "Unknown",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "opening@customer.example", Address = Customer }]
    };

    private static readonly SupportAnswerRoute Mailed = new() { ByMail = true, To = Customer, From = "support@example.net", Mailbox = "primary" };

    private readonly Mock<ISupportCaseService> _cases = new();

    private void Register(SupportAnswerRoute route, string teamKey = TeamKey)
    {
        _cases.Setup(x => x.GetMessagesAsync(teamKey, Case.Id, It.IsAny<string>(), It.IsAny<int>(), default))
            .ReturnsAsync(new SupportMessagePage { Items = [new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorName = "Unknown", Body = "The export is empty.", SentAt = Case.CreatedAt }] });
        _cases.Setup(x => x.GetAnswerRouteAsync(teamKey, Case.Id, default)).ReturnsAsync(route);
        _cases.Setup(x => x.PreviewAnswerAsync(teamKey, Case.Id, It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(new MailPreview { From = "Example Support <support@example.net>", To = Customer, Subject = "Re: Export is empty", Html = "<html><body><p>sam:</p></body></html>", Text = "sam:" });

        Services.AddSingleton(_cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();

        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", true));
    }

    private IRenderedComponent<SupportAnswerRouteLine> Line(SupportAnswerRoute route)
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();

        return Render<SupportAnswerRouteLine>(p => p.Add(x => x.Route, route));
    }

    [Fact]
    public void InProduction_TheLineNamesTheAddressAndTheMailbox_AndNothingMore()
    {
        var line = Line(Mailed);

        Assert.Equal($"The answer is mailed to {Customer} from support@example.net.", line.Find("[data-answer-to]").TextContent.Trim());
        Assert.Empty(line.FindAll("[data-answer-redirected]"));
        Assert.Empty(line.FindAll("[data-answer-withheld]"));
    }

    [Fact]
    public void WhenRedirected_TheLineSaysWhereTo()
    {
        var line = Line(Mailed with { Effect = MailPolicyEffect.Redirect, RedirectedTo = "testers@example.net" });

        Assert.Equal("In this environment the answer is redirected to testers@example.net.", line.Find("[data-answer-redirected]").TextContent.Trim());
    }

    [Fact]
    public void WhenWithheld_TheLineSaysItIsNotSent()
    {
        var line = Line(Mailed with { Effect = MailPolicyEffect.Withhold });

        Assert.Equal(SupportAnswerRouteText.Withheld.Default, line.Find("[data-answer-withheld]").TextContent.Trim());
    }

    [Fact]
    public void ACaseWithNoMailThread_IsAnsweredOnTheSite()
    {
        var line = Line(SupportAnswerRoute.Site);

        Assert.Equal(SupportAnswerRouteText.Site.Default, line.Find("[data-answer-site]").TextContent.Trim());
        Assert.Empty(line.FindAll("[data-answer-to]"));
    }

    [Fact]
    public void WithNoKnownSendingAddress_TheLineNamesTheRecipientAlone()
    {
        Assert.Equal($"The answer is mailed to {Customer}.", Line(Mailed with { From = null }).Find("[data-answer-to]").TextContent.Trim());
    }

    [Fact]
    public void WithNoRoute_NothingIsShown()
    {
        Assert.Empty(Line(null).Markup.Trim());
    }

    [Fact]
    public async Task TheUnassignedAnswerBox_ShowsTheRoute_AndPreviewsTheDraftAsMailed()
    {
        Register(Mailed, null);
        var dialogs = Render<RadzenDialog>();
        var view = Render<SupportUnassignedConversation>(p => p.Add(x => x.Case, Case with { TeamKey = null }));

        Assert.Contains(Customer, view.Find("[data-answer-to]").TextContent);
        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Try <b>this</b>.</p>"));

        await view.InvokeAsync(() => view.Find("[data-preview-answer]").Click());

        _cases.Verify(x => x.PreviewAnswerAsync(null, Case.Id, string.Empty, "<p>Try <b>this</b>.</p>", default), Times.Once);
        var frame = dialogs.Find("iframe[data-mail-frame]");
        Assert.Equal(string.Empty, frame.GetAttribute("sandbox"));
        Assert.Equal("<html><body><p>sam:</p></body></html>", frame.GetAttribute("srcdoc"));
        Assert.Contains(Customer, dialogs.Find("[data-preview-header='to']").TextContent);
        _cases.Verify(x => x.AnswerCaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<SupportAttachmentUpload>>(), default), Times.Never);
    }

    [Fact]
    public void AnAnswerShownOnTheSite_OffersNoPreview()
    {
        Register(SupportAnswerRoute.Site, null);

        var view = Render<SupportUnassignedConversation>(p => p.Add(x => x.Case, Case with { TeamKey = null }));

        Assert.NotNull(view.Find("[data-answer-site]"));
        Assert.Empty(view.FindAll("[data-preview-answer]"));
    }

    [Fact]
    public void AReaderWhoMayNotAnswer_IsShownNoRoute()
    {
        Register(Mailed, null);
        _cases.Setup(x => x.GetAnswerRouteAsync(null, Case.Id, default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = Render<SupportUnassignedConversation>(p => p.Add(x => x.Case, Case with { TeamKey = null }));

        Assert.Empty(view.FindAll("[data-answer-route]"));
        Assert.Empty(view.FindAll("[data-preview-answer]"));
    }

    [Fact]
    public async Task TheAcrossTeamsAnswerBox_ShowsTheRoute_AndPreviews()
    {
        Register(Mailed with { Effect = MailPolicyEffect.Redirect, RedirectedTo = "testers@example.net" });
        var dialogs = Render<RadzenDialog>();

        var view = Render<SupportCaseConversation>(p => p.Add(x => x.Case, Case).Add(x => x.AsStaff, true));
        await view.InvokeAsync(() => view.Find("[data-preview-answer]").Click());

        Assert.Contains("testers@example.net", view.Find("[data-answer-redirected]").TextContent);
        _cases.Verify(x => x.PreviewAnswerAsync(TeamKey, Case.Id, string.Empty, string.Empty, default), Times.Once);
        Assert.NotNull(dialogs.Find("iframe[data-mail-frame]"));
    }

    [Fact]
    public void AMembersReplyBox_ShowsNoRoute_AndDoesNotAskForOne()
    {
        Register(Mailed);

        var view = Render<SupportCaseConversation>(p => p.Add(x => x.Case, Case).Add(x => x.AsStaff, false));

        Assert.Empty(view.FindAll("[data-answer-route]"));
        Assert.Empty(view.FindAll("[data-preview-answer]"));
        _cases.Verify(x => x.GetAnswerRouteAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task TheTeamsQueue_ShowsTheRoute_OfTheCaseOpened()
    {
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);
        Services.AddSingleton(teamState.Object);
        Register(Mailed with { Effect = MailPolicyEffect.Withhold });
        _cases.Setup(x => x.GetCasesAsync(TeamKey, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportCasePage { Items = [Case] });
        _cases.Setup(x => x.GetCaseAsync(TeamKey, Case.Id, default)).ReturnsAsync(Case);

        var view = Render<SupportQueueView>();
        Assert.Empty(view.FindAll("[data-answer-route]"));

        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Export is empty")).Click());

        Assert.NotNull(view.Find("[data-answer-withheld]"));
        Assert.NotNull(view.Find("[data-preview-answer]"));
    }
}
