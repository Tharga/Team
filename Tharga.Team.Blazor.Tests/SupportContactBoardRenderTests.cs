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
/// Working a contact-form message on the support boards: the contact as a table, copy buttons, searching the organisations, the
/// contact after the case has a team, the host's own content in each opened case, and opening a case by id.
/// </summary>
/// <remarks>
/// Rendered and clicked, because a handler on a parameter that does not exist is swallowed as an attribute and ships a dead
/// control. Addresses are made up, on more than one domain.
/// </remarks>
public class SupportContactBoardRenderTests : BunitContext
{
    private static readonly DateTime Morning = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Consent = new(2026, 11, 8, 17, 30, 0, DateTimeKind.Utc);

    private static readonly ExternalSupportContact Contact = new()
    {
        Name = "Vera Visitor",
        Email = "vera@acme.example",
        Phone = "555 0100",
        Organisation = "Acme",
        ConsentGivenAt = Consent,
        Fields = new Dictionary<string, string> { ["Product"] = "Export", ["Size"] = "Large", ["Empty"] = "  " }
    };

    private static SupportCase Unassigned(string id = "case-1", ExternalSupportContact contact = null) => new()
    {
        Id = id,
        TeamKey = null,
        AuthorIdentity = null,
        AuthorName = "Vera Visitor",
        Subject = "Question " + id,
        Status = SupportCaseStatus.Open,
        CreatedAt = Morning,
        MessageCount = 1,
        ExternalContact = contact ?? Contact
    };

    private readonly Mock<ISupportCaseService> _cases = new();

    public SupportContactBoardRenderTests()
    {
        _cases.Setup(x => x.GetAwaitingUnassignedCountAsync(default)).ReturnsAsync(0);
        _cases.Setup(x => x.GetAwaitingSupportCountAcrossTeamsAsync(default)).ReturnsAsync(0);
        _cases.Setup(x => x.GetAttachmentLimitsAsync(default)).ReturnsAsync(SupportAttachmentLimits.Default);
        _cases.Setup(x => x.GetMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), null, SupportCaseLimits.MaxMessagesPerCase, default))
            .ReturnsAsync(new SupportMessagePage { Items = [new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorName = "Vera", Body = "Hello.", SentAt = Morning }] });
        _cases.Setup(x => x.GetAnswerRouteAsync(It.IsAny<string>(), It.IsAny<string>(), default)).ReturnsAsync(SupportAnswerRoute.Site);

        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<SupportUnassignedView> Queue(SupportCase[] items, Action<ComponentParameterCollectionBuilder<SupportUnassignedView>> set = null, bool oversight = false)
    {
        _cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = items });
        Services.AddSingleton(_cases.Object);
        if (oversight) Services.AddSingleton(Oversight());

        SetRendererInfo(new RendererInfo("Server", true));

        return Render<SupportUnassignedView>(p => set?.Invoke(p));
    }

    private IRenderedComponent<SupportAcrossTeamsView> AcrossTeams(SupportCase[] items, Action<ComponentParameterCollectionBuilder<SupportAcrossTeamsView>> set = null)
    {
        _cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = items });
        Services.AddSingleton(_cases.Object);

        SetRendererInfo(new RendererInfo("Server", true));

        return Render<SupportAcrossTeamsView>(p => set?.Invoke(p));
    }

    private static void Open<T>(IRenderedComponent<T> view, string caseId) where T : IComponent
        => view.Find($"[data-case='{caseId}'] [style*='cursor: pointer']").Click();

    private static string[] Rows<T>(IRenderedComponent<T> view, string caseId) where T : IComponent
        => view.FindAll($"[data-contact='{caseId}'] [data-contact-row]").Select(x => x.GetAttribute("data-contact-row")).ToArray();

    [Fact]
    public void TheContact_IsATable_OfLabelAndValue_InTheOrderTheyArrive()
    {
        var view = Queue([Unassigned()]);
        Open(view, "case-1");

        Assert.Equal(["name", "email", "phone", "organisation", "field:Product", "field:Size", "consent"], Rows(view, "case-1"));
        Assert.Equal("Vera Visitor", view.Find("[data-contact-value='name']").TextContent);
        Assert.Equal("vera@acme.example", view.Find("[data-contact-value='email']").TextContent);
        Assert.Equal("Export", view.Find("[data-contact-value='field:Product']").TextContent);
        Assert.Equal(Consent.ToLocalTime().ToString("g"), view.Find("[data-contact-value='consent']").TextContent);
        Assert.Equal("E-mail", view.Find("[data-contact-row='email'] th").TextContent);
    }

    [Fact]
    public void RowsWithNothingInThem_AreLeftOut_ButNoConsentIsSaid()
    {
        var view = Queue([Unassigned(contact: new ExternalSupportContact { Email = "buyer@sample.test" })]);
        Open(view, "case-1");

        Assert.Equal(["email", "consent"], Rows(view, "case-1"));
        Assert.Equal(SupportContactText.NoConsent.Default, view.Find("[data-contact-value='consent']").TextContent);
    }

    [Fact]
    public async Task TheLabels_GoThroughTheTextProvider_SoAHostCanTranslateThem()
    {
        Services.AddSingleton<IThargaTextProvider>(new Translating(new Dictionary<string, string>
        {
            [SupportContactText.Email.Key] = "E-post",
            [SupportContactText.Phone.Key] = "Telefon"
        }));

        var view = Render<SupportContactTable>(p => p.Add(x => x.Contact, Contact).Add(x => x.CaseId, "case-1"));
        await Task.Yield();

        Assert.Equal("E-post", view.Find("[data-contact-row='email'] th").TextContent);
        Assert.Equal("Telefon", view.Find("[data-contact-row='phone'] th").TextContent);
    }

    [Fact]
    public void TheAddressAndThePhoneNumber_HaveACopyButton_AndNothingElseDoes()
    {
        var view = Queue([Unassigned()]);
        Open(view, "case-1");

        Assert.Equal(["email", "phone"], view.FindAll("[data-contact='case-1'] [data-copy]").Select(x => x.GetAttribute("data-copy")).ToArray());
    }

    [Fact]
    public void Copying_PutsTheValueOnTheClipboard_AndSaysSo()
    {
        var view = Queue([Unassigned()]);
        Open(view, "case-1");

        view.Find("[data-copy='email']").Click();
        view.WaitForAssertion(() => Assert.Single(JSInterop.Invocations, x => x.Identifier == "clipboard.copyText"));
        view.Find("[data-copy='phone']").Click();
        view.WaitForAssertion(() => Assert.Equal(2, JSInterop.Invocations.Count(x => x.Identifier == "clipboard.copyText")));

        var copies = JSInterop.Invocations.Where(x => x.Identifier == "clipboard.copyText").Select(x => (string)x.Arguments[0]).ToArray();
        Assert.Equal(["vera@acme.example", "555 0100"], copies);
        var notices = Services.GetRequiredService<NotificationService>().Messages;
        Assert.Contains(notices, x => x.Summary == SupportContactText.Copied.Default);
    }

    [Fact]
    public void WhenTheBrowserRefusesTheClipboard_NothingIsClaimedAsCopied()
    {
        JSInterop.SetupVoid("clipboard.copyText", _ => true).SetException(new Microsoft.JSInterop.JSException("denied"));
        var view = Queue([Unassigned()]);
        Open(view, "case-1");

        view.Find("[data-copy='email']").Click();
        view.WaitForAssertion(() => Assert.Single(JSInterop.Invocations, x => x.Identifier == "clipboard.copyText"));

        Assert.Empty(Services.GetRequiredService<NotificationService>().Messages);
    }

    [Fact]
    public void TheOrganisationList_CanBeSearched_AnywhereInTheName_WithoutRegardToCase()
    {
        var view = Queue([Unassigned()], oversight: true);
        Open(view, "case-1");

        var list = view.FindComponent<RadzenDropDown<string>>().Instance;

        Assert.True(list.AllowFiltering);
        Assert.Equal(FilterCaseSensitivity.CaseInsensitive, list.FilterCaseSensitivity);
        Assert.Equal(StringFilterOperator.Contains, list.FilterOperator);
    }

    [Fact]
    public void AfterTheCaseHasATeam_TheContactStillShows_OnTheAcrossTeamsBoard()
    {
        var assigned = Unassigned() with { TeamKey = "acme" };
        var view = AcrossTeams([assigned]);
        Open(view, "case-1");

        Assert.Equal(["name", "email", "phone", "organisation", "field:Product", "field:Size", "consent"], Rows(view, "case-1"));
    }

    [Fact]
    public void ACaseThatDidNotComeFromTheForm_ShowsNoContact()
    {
        var plain = Unassigned() with { TeamKey = "acme", ExternalContact = null };
        var view = AcrossTeams([plain]);
        Open(view, "case-1");

        Assert.Empty(view.FindAll("[data-contact]"));
    }

    [Fact]
    public void TheHostsContent_IsRenderedInEachOpenedCase_WithTheCaseAsItsContext_BelowTheContact()
    {
        RenderFragment<SupportCase> extras = c => b => b.AddMarkupContent(0, $"<div class=\"host-extra\" data-extra=\"{c.Id}\">linked {c.Subject}</div>");
        var view = Queue([Unassigned("a"), Unassigned("b")], p => p.Add(x => x.CaseExtras, extras));

        Assert.Empty(view.FindAll(".host-extra"));

        Open(view, "a");
        Open(view, "b");

        Assert.Equal("linked Question a", view.Find("[data-case='a'] [data-extra='a']").TextContent);
        Assert.Equal("linked Question b", view.Find("[data-case='b'] [data-extra='b']").TextContent);
        var markup = view.Find("[data-case='a'] [data-conversation]").InnerHtml;
        Assert.True(markup.IndexOf("data-contact=", StringComparison.Ordinal) < markup.IndexOf("host-extra", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHostsContent_AlsoShowsOnTheAcrossTeamsBoard()
    {
        RenderFragment<SupportCase> extras = c => b => b.AddMarkupContent(0, $"<div class=\"host-extra\">linked {c.Id}</div>");
        var view = AcrossTeams([Unassigned() with { TeamKey = "acme" }], p => p.Add(x => x.CaseExtras, extras));

        Open(view, "case-1");

        Assert.Equal("linked case-1", view.Find(".host-extra").TextContent);
    }

    [Fact]
    public void AViewGivenACaseId_OpensThatCaseOnLoad()
    {
        var view = Queue([Unassigned("a"), Unassigned("b")], p => p.Add(x => x.CaseId, "b"));

        Assert.NotEmpty(view.FindAll("[data-case='b'] [data-conversation='b']"));
        Assert.Empty(view.FindAll("[data-case='a'] [data-conversation='a']"));
    }

    [Fact]
    public void AnUnknownCaseId_OpensNothing_AndDoesNotThrow()
    {
        var view = Queue([Unassigned("a")], p => p.Add(x => x.CaseId, "no-such-case"));

        Assert.Empty(view.FindAll("[data-conversation]"));
        Assert.NotEmpty(view.FindAll("[data-case='a']"));
    }

    [Fact]
    public void TheAcrossTeamsBoard_AlsoOpensAGivenCase()
    {
        var view = AcrossTeams([Unassigned("a") with { TeamKey = "acme" }, Unassigned("b") with { TeamKey = "acme" }], p => p.Add(x => x.CaseId, "a"));

        Assert.NotEmpty(view.FindAll("[data-case='a'] [data-conversation='a']"));
    }

    [Fact]
    public void ANewCaseIdGiven_LaterOpensThatCase()
    {
        var view = Queue([Unassigned("a"), Unassigned("b")], p => p.Add(x => x.CaseId, "a"));

        view.Render(p => p.Add(x => x.CaseId, "b"));

        Assert.NotEmpty(view.FindAll("[data-case='b'] [data-conversation='b']"));
    }

    private static ITeamOversightService Oversight()
    {
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns("acme");
        team.SetupGet(x => x.Name).Returns("Acme");
        var oversight = new Mock<ITeamOversightService>();
        oversight.Setup(x => x.GetAllTeamsAsync()).Returns(AsyncOf(team.Object));

        return oversight.Object;
    }

    private static async IAsyncEnumerable<ITeam> AsyncOf(params ITeam[] teams)
    {
        foreach (var team in teams) yield return team;

        await Task.CompletedTask;
    }

    private sealed class Translating(Dictionary<string, string> texts) : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => Task.FromResult(texts.GetValueOrDefault(key.Key) ?? key.Default);
    }
}
