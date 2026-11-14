using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Blazor;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Support staff writing first: the "New message" button on the boards, the compose form a host can place, and the
/// conversation of a case shown by its id.
/// </summary>
/// <remarks>
/// Rendered and clicked rather than asserted through the service, because a handler on a parameter that does not exist is
/// swallowed as an attribute and ships a dead control. Addresses are made up, on more than one domain.
/// </remarks>
public class SupportStartMailRenderTests : BunitContext
{
    private static readonly DateTime Morning = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);

    private static readonly SupportAnswerRoute Route = new()
    {
        ByMail = true,
        To = "lead@acme.example",
        From = "support@example.net",
        Senders =
        [
            new SupportAnswerSender { Address = "support@example.net", Name = "Example Support" },
            new SupportAnswerSender { Address = "help@example.net", Name = "Example Help" }
        ]
    };

    private static readonly SupportCase Started = new()
    {
        Id = "started-1",
        TeamKey = null,
        AuthorIdentity = null,
        AuthorName = "A Prospect",
        Subject = "Our offer",
        Status = SupportCaseStatus.Open,
        CreatedAt = Morning,
        MessageCount = 1,
        ExternalContact = new ExternalSupportContact { Email = "lead@acme.example", Name = "A Prospect" }
    };

    private readonly Mock<ISupportCaseService> _cases = new();

    public SupportStartMailRenderTests()
    {
        _cases.Setup(x => x.GetStartMailRouteAsync(It.IsAny<string>(), default)).ReturnsAsync(Route);
        _cases.Setup(x => x.StartMailCaseAsync(It.IsAny<StartMailCaseRequest>(), default)).ReturnsAsync(Started);
        _cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = [] });
        _cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = [] });
        _cases.Setup(x => x.GetAttachmentLimitsAsync(default)).ReturnsAsync(SupportAttachmentLimits.Default);

        Services.AddSingleton(_cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void TheForm_StartsFromWhatTheHostGives()
    {
        var view = Render<SupportComposeMail>(p => p
            .Add(x => x.To, "lead@acme.example")
            .Add(x => x.ToName, "A Prospect")
            .Add(x => x.Subject, "Our offer"));

        Assert.Equal("lead@acme.example", view.Find("[data-compose-to]").GetAttribute("value"));
        Assert.Equal("A Prospect", view.Find("[data-compose-name]").GetAttribute("value"));
        Assert.Equal("Our offer", view.Find("[data-compose-subject]").GetAttribute("value"));
        _cases.Verify(x => x.GetStartMailRouteAsync("lead@acme.example", default), Times.AtLeastOnce);
    }

    [Fact]
    public void TheForm_OffersTheSendFromPicker_AndTheSignatureOption()
    {
        var view = Render<SupportComposeMail>(p => p.Add(x => x.To, "lead@acme.example"));

        Assert.NotNull(view.Find("[data-answer-sender]"));
        Assert.NotNull(view.Find("[data-answer-sign]"));
    }

    [Fact]
    public void WithNoMessage_NothingCanBeSent()
    {
        var view = Render<SupportComposeMail>(p => p.Add(x => x.To, "lead@acme.example"));

        Assert.True(view.Find("[data-compose-send]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Sending_StartsTheCase_WithWhatWasWritten_AndHandsItBack()
    {
        SupportCase handedBack = null;
        var view = Render<SupportComposeMail>(p => p
            .Add(x => x.To, "lead@acme.example")
            .Add(x => x.ToName, "A Prospect")
            .Add(x => x.Subject, "Our offer")
            .Add(x => x.TeamKey, "acme")
            .Add(x => x.Started, EventCallback.Factory.Create<SupportCase>(this, c => handedBack = c)));
        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Hello <b>there</b>.</p>"));

        await view.InvokeAsync(() => view.Find("[data-compose-send]").Click());

        _cases.Verify(x => x.StartMailCaseAsync(It.Is<StartMailCaseRequest>(r =>
            r.To == "lead@acme.example" && r.ToName == "A Prospect" && r.Subject == "Our offer" && r.TeamKey == "acme" && r.BodyHtml.Contains("Hello")), default), Times.Once);
        Assert.Equal("started-1", handedBack?.Id);
    }

    [Fact]
    public async Task TheContactTheHostKnows_IsKeptOnTheCase()
    {
        var contact = new ExternalSupportContact { Email = "ignored@acme.example", Organisation = "Acme", Phone = "555 0100" };
        var view = Render<SupportComposeMail>(p => p.Add(x => x.To, "lead@acme.example").Add(x => x.Contact, contact));
        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Hi.</p>"));

        await view.InvokeAsync(() => view.Find("[data-compose-send]").Click());

        _cases.Verify(x => x.StartMailCaseAsync(It.Is<StartMailCaseRequest>(r => r.Contact == contact), default), Times.Once);
    }

    [Fact]
    public async Task ARefusedAddress_IsShown_AndNothingIsHandedBack()
    {
        _cases.Setup(x => x.StartMailCaseAsync(It.IsAny<StartMailCaseRequest>(), default)).ThrowsAsync(new ArgumentException("An email address is required.", "contact"));
        var handedBack = false;
        var view = Render<SupportComposeMail>(p => p
            .Add(x => x.To, "nobody")
            .Add(x => x.Started, EventCallback.Factory.Create<SupportCase>(this, _ => handedBack = true)));
        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Hi.</p>"));

        await view.InvokeAsync(() => view.Find("[data-compose-send]").Click());

        Assert.Contains(SupportComposeMailText.BadAddress.Default, view.Find("[data-compose-error]").TextContent);
        Assert.False(handedBack);
    }

    [Fact]
    public void SomebodyWhoMayNotStartACase_IsToldSo_AndSeesNoForm()
    {
        _cases.Setup(x => x.GetStartMailRouteAsync(It.IsAny<string>(), default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = Render<SupportComposeMail>();

        Assert.Contains(SupportComposeMailText.NotPermitted.Default, view.Find("[data-compose-problem]").TextContent);
        Assert.Empty(view.FindAll("[data-compose-mail]"));
    }

    [Fact]
    public void WithNoMailSetUp_TheFormSaysSo()
    {
        _cases.Setup(x => x.GetStartMailRouteAsync(It.IsAny<string>(), default)).ThrowsAsync(new InvalidOperationException("no mail"));

        var view = Render<SupportComposeMail>();

        Assert.Contains(SupportComposeMailText.NoMail.Default, view.Find("[data-compose-problem]").TextContent);
    }

    [Fact]
    public void WhenTeamsAreGiven_TheReaderCanPlaceTheCase()
    {
        var view = Render<SupportComposeMail>(p => p.Add(x => x.Teams, [new SupportCaseTeam { Key = "acme", Name = "Acme" }]));

        Assert.NotEmpty(view.FindAll("[data-compose-team]"));
    }

    [Fact]
    public void WithNoTeams_NoChoiceIsOffered()
    {
        var view = Render<SupportComposeMail>();

        Assert.Empty(view.FindAll("[data-compose-team]"));
    }

    [Fact]
    public void TheUnassignedBoard_OffersNewMessage_AndOpensTheForm()
    {
        var dialogs = Render<RadzenDialog>();
        var view = Render<SupportUnassignedView>();

        view.Find("[data-new-message]").Click();

        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll("[data-compose-mail]")));
        Assert.Contains(SupportComposeMailText.NewMessage.Default, dialogs.Markup);
    }

    [Fact]
    public void TheAcrossTeamsBoard_OffersNewMessage_AndOpensTheForm()
    {
        var dialogs = Render<RadzenDialog>();
        var view = Render<SupportAcrossTeamsView>();

        view.Find("[data-new-message]").Click();

        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll("[data-compose-mail]")));
    }

    [Fact]
    public async Task SendingFromTheDialog_ClosesIt()
    {
        var dialogs = Render<RadzenDialog>();
        var view = Render<SupportUnassignedView>();
        view.Find("[data-new-message]").Click();
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll("[data-compose-mail]")));
        await dialogs.InvokeAsync(() => ((IRenderedComponent<IComponent>)dialogs).FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Hi.</p>"));
        await dialogs.InvokeAsync(() => dialogs.Find("[data-compose-to]").Input("lead@acme.example"));

        await dialogs.InvokeAsync(() => dialogs.Find("[data-compose-send]").Click());

        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll("[data-compose-mail]")));
        _cases.Verify(x => x.StartMailCaseAsync(It.Is<StartMailCaseRequest>(r => r.To == "lead@acme.example"), default), Times.Once);
    }

    [Fact]
    public void ACaseById_WithNoTeam_ShowsItsConversation()
    {
        Cases(Started);

        var view = Render<SupportCaseById>(p => p.Add(x => x.CaseId, "started-1"));

        Assert.NotEmpty(view.FindAll("[data-conversation='started-1']"));
        Assert.Contains("Hello, here is our offer.", view.Markup);
    }

    [Fact]
    public void ACaseById_InATeam_ShowsItsConversation_ReadWithTheTeamItReports()
    {
        var inTeam = Started with { Id = "team-case", TeamKey = "acme" };
        Cases(inTeam);

        var view = Render<SupportCaseById>(p => p.Add(x => x.CaseId, "team-case"));

        Assert.NotEmpty(view.FindAll("[data-conversation='team-case']"));
        _cases.Verify(x => x.GetMessagesAsync("acme", "team-case", null, SupportCaseLimits.MaxMessagesPerCase, default), Times.AtLeastOnce);
    }

    [Fact]
    public void AnUnknownId_ShowsNothingButANotice_AndDoesNotThrow()
    {
        _cases.Setup(x => x.GetCaseByIdAsync("nope", default)).ReturnsAsync((SupportCase)null);

        var view = Render<SupportCaseById>(p => p.Add(x => x.CaseId, "nope"));

        Assert.Contains(SupportCaseByIdText.NotFound.Default, view.Find("[data-case-not-found]").TextContent);
    }

    [Fact]
    public void AnUnknownId_ShowsTheHostsOwnNotice_WhenItGivesOne()
    {
        _cases.Setup(x => x.GetCaseByIdAsync("nope", default)).ReturnsAsync((SupportCase)null);

        var view = Render<SupportCaseById>(p => p
            .Add(x => x.CaseId, "nope")
            .Add(x => x.NotFound, (RenderFragment)(b => b.AddMarkupContent(0, "<p class=\"host-notice\">No conversation yet.</p>"))));

        Assert.Equal("No conversation yet.", view.Find(".host-notice").TextContent);
        Assert.Empty(view.FindAll("[data-case-not-found]"));
    }

    [Fact]
    public void SomebodyWhoMayNotReadTheCase_IsToldSo_WithoutItsContents()
    {
        _cases.Setup(x => x.GetCaseByIdAsync("started-1", default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = Render<SupportCaseById>(p => p.Add(x => x.CaseId, "started-1"));

        Assert.Contains(SupportCaseByIdText.NotPermitted.Default, view.Find("[data-case-denied]").TextContent);
        Assert.DoesNotContain("Our offer", view.Markup);
    }

    [Fact]
    public async Task TheCaseRead_IsHandedToTheHost()
    {
        Cases(Started);
        SupportCase loaded = null;

        Render<SupportCaseById>(p => p
            .Add(x => x.CaseId, "started-1")
            .Add(x => x.Loaded, EventCallback.Factory.Create<SupportCase>(this, c => loaded = c)));
        await Task.Yield();

        Assert.Equal("started-1", loaded?.Id);
    }

    private void Cases(SupportCase supportCase)
    {
        _cases.Setup(x => x.GetCaseByIdAsync(supportCase.Id, default)).ReturnsAsync(supportCase);
        _cases.Setup(x => x.GetMessagesAsync(supportCase.TeamKey, supportCase.Id, null, SupportCaseLimits.MaxMessagesPerCase, default))
            .ReturnsAsync(new SupportMessagePage
            {
                Items =
                [
                    new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorName = "Anna", Body = "Hello, here is our offer.", SentAt = Morning, AsSupport = true }
                ]
            });
        _cases.Setup(x => x.GetAnswerRouteAsync(supportCase.TeamKey, supportCase.Id, default)).ReturnsAsync(Route);
    }
}
