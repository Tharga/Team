using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Blazor;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The case lists show what is going on: each case says what kind of conversation it is and when it last moved,
/// the liveliest first, with closed cases out of the way until asked for — and the choice remembered.
/// </summary>
public class SupportCaseListRenderTests : BunitContext
{
    private const string TeamKey = "acme";
    private static readonly DateTime Morning = new(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);

    private static SupportCase Case(string id, int minutesLater, SupportCaseStatus status = SupportCaseStatus.Open,
        SupportAssistantState assistant = SupportAssistantState.None, bool awaiting = false) => new()
    {
        Id = id,
        TeamKey = TeamKey,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = $"Subject {id}",
        Status = status,
        CreatedAt = Morning,
        LastMessageAt = Morning.AddMinutes(minutesLater),
        MessageCount = 1,
        AssistantState = assistant,
        AwaitingSupport = awaiting
    };

    private static readonly SupportCase[] Cases =
    [
        Case("older", 10),
        Case("closed", 50, status: SupportCaseStatus.Closed),
        Case("assistant", 60, assistant: SupportAssistantState.Active),
        Case("newest", 40)
    ];

    [Fact]
    public void ByDefault_OpenSupportCasesAreShown_TheLiveliestFirst()
    {
        var view = RenderCases(out _);

        Assert.Equal(["newest", "older"], CaseIds(view));
    }

    /// <remarks>A conversation with the assistant is neither open nor closed, so the status filter lets it through.</remarks>
    [Fact]
    public async Task ChoosingTheAssistantToo_ShowsItsConversations_WhateverTheStatus()
    {
        var view = RenderCases(out _);

        await ChooseKindsAsync(view, SupportAssistance.None, SupportAssistance.Assistant);

        Assert.Equal(["assistant", "newest", "older"], CaseIds(view));
    }

    [Fact]
    public async Task ChoosingClosedToo_BringsThemBack()
    {
        var view = RenderCases(out _);

        var status = view.FindComponent<RadzenSelectBar<IEnumerable<SupportCaseStatus>>>();
        await view.InvokeAsync(() => status.Instance.Change.InvokeAsync(new[] { SupportCaseStatus.Open, SupportCaseStatus.Closed }));

        Assert.Contains("closed", CaseIds(view));
    }

    [Fact]
    public async Task ChoosingNoStatus_ShowsEveryCase()
    {
        var view = RenderCases(out _);

        var status = view.FindComponent<RadzenSelectBar<IEnumerable<SupportCaseStatus>>>();
        await view.InvokeAsync(() => status.Instance.Change.InvokeAsync(Array.Empty<SupportCaseStatus>()));
        await ChooseKindsAsync(view);

        Assert.Equal(4, CaseIds(view).Length);
    }

    [Fact]
    public async Task TheFilterIsRememberedInTheBrowser()
    {
        var view = RenderCases(out _);

        var kind = view.FindComponent<RadzenSelectBar<IEnumerable<SupportAssistance>>>();
        await view.InvokeAsync(() => kind.Instance.Change.InvokeAsync(new[] { SupportAssistance.Assistant }));

        var stored = JSInterop.VerifyInvoke("localStorage.setItem").Arguments;
        Assert.Contains("Assistant", stored[1]?.ToString());
        Assert.Equal(["assistant"], CaseIds(view));
    }

    [Fact]
    public void ARememberedFilter_IsRestored()
    {
        JSInterop.Setup<string>("localStorage.getItem", _ => true).SetResult("{\"Statuses\":[\"Closed\"],\"Kinds\":[\"None\"]}");

        var view = RenderCases(out _);

        view.WaitForAssertion(() => Assert.Equal(["closed"], CaseIds(view)));
    }

    [Fact]
    public async Task EachCaseSaysWhatKindOfConversationItIs_BeforeItIsOpened()
    {
        var view = RenderCases(out _);
        await ChooseKindsAsync(view, SupportAssistance.None, SupportAssistance.Assistant);

        Assert.Equal(["smart_toy", "support_agent", "support_agent"], view.FindComponents<ExpandableCard>().Select(x => x.Instance.Icon).ToArray());
        Assert.Contains("Assistant conversation", view.Find("[data-status='assistant']").TextContent);
        Assert.DoesNotContain("The opening line.", view.Markup);
    }

    [Fact]
    public void TheCasesComeFirst_AndTalkingToSomebodyIsTheSecondTab()
    {
        var view = RenderCases(out _);

        var tabs = view.FindComponents<RadzenTabsItem>().Select(x => x.Instance.Text).ToArray();

        Assert.Equal(["Cases", "Talk to"], tabs);
        Assert.Equal(0, view.FindComponent<RadzenTabs>().Instance.SelectedIndex);
    }

    [Theory]
    [InlineData(SupportCasesView.NewCaseTab, 1)]
    [InlineData(SupportCasesView.CasesTab, 0)]
    public void ANamedTab_IsTheOneShown(string tab, int index)
    {
        var view = RenderCases(out _, tab: tab);

        Assert.Equal(index, view.FindComponent<RadzenTabs>().Instance.SelectedIndex);
    }

    [Fact]
    public async Task ChangingTab_TellsTheHostItsName()
    {
        string told = null;
        var view = RenderCases(out _, tabChanged: x => told = x);

        await view.InvokeAsync(() => view.FindComponent<RadzenTabs>().Instance.SelectedIndexChanged.InvokeAsync(1));

        Assert.Equal(SupportCasesView.NewCaseTab, told);
    }

    [Fact]
    public void WithNoCaseYet_ItOpensWhereHelpIsAskedFor()
    {
        var view = RenderCases(out _, items: []);

        Assert.Equal(1, view.FindComponent<RadzenTabs>().Instance.SelectedIndex);
    }

    [Fact]
    public async Task AConversationWithTheAssistant_HasNothingToClose()
    {
        var view = RenderCases(out _);
        await ChooseKindsAsync(view, SupportAssistance.Assistant);

        OpenCase(view, "assistant");

        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent.Contains("Close case"));
    }

    [Fact]
    public void WithAnAssistantToOffer_WhoToTalkToIsASelectBar()
    {
        Services.AddSingleton(Mock.Of<ISupportResponder>());

        var view = RenderCases(out _);

        Assert.Single(view.FindComponents<RadzenSelectBar<SupportAssistance>>());
    }

    [Fact]
    public void WithOnlySupportToOffer_ThereIsNothingToChoose()
    {
        var view = RenderCases(out _);

        Assert.Empty(view.FindComponents<RadzenSelectBar<SupportAssistance>>());
    }

    [Fact]
    public async Task TheAuthorClosesTheirOwnCase_AfterConfirmingIt()
    {
        var view = RenderCases(out var cases);
        var dialogs = Render<RadzenDialog>();

        OpenCase(view, "newest");
        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Close case")).Click());

        cases.Verify(x => x.CloseCaseAsync(TeamKey, "newest", default), Times.Never);
        Assert.Single(dialogs.FindAll("[data-close-dialog='team']"));

        await dialogs.InvokeAsync(() => dialogs.Find("[data-confirm-close]").Click());

        cases.Verify(x => x.CloseCaseAsync(TeamKey, "newest", default), Times.Once);
    }

    /// <summary>The dialog offers who reads the case from now on, and closing with another choice makes it.</summary>
    [Fact]
    public async Task ClosingOffersWhoReadsTheCase_AndKeepsTheChoice()
    {
        var view = RenderCases(out var cases);
        var dialogs = Render<RadzenDialog>();

        OpenCase(view, "newest");
        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Close case")).Click());

        var picker = dialogs.FindComponent<SupportCaseVisibilityPicker>();
        await dialogs.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(SupportCaseVisibility.Private));
        await dialogs.InvokeAsync(() => dialogs.Find("[data-confirm-close]").Click());

        cases.Verify(x => x.SetCaseVisibilityAsync(TeamKey, "newest", SupportCaseVisibility.Private, default), Times.Once);
        cases.Verify(x => x.CloseCaseAsync(TeamKey, "newest", default), Times.Once);
    }

    [Fact]
    public async Task CancellingTheDialog_LeavesTheCaseOpen()
    {
        var view = RenderCases(out var cases);
        var dialogs = Render<RadzenDialog>();

        OpenCase(view, "newest");
        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Close case")).Click());
        await dialogs.InvokeAsync(() => dialogs.FindAll("[data-close-dialog] button").First(b => b.TextContent.Contains("Cancel")).Click());

        cases.Verify(x => x.CloseCaseAsync(TeamKey, It.IsAny<string>(), default), Times.Never);
    }

    /// <summary>Staff may close any time, but are told first that it is better left to the organisation.</summary>
    [Fact]
    public async Task StaffAreWarned_BeforeTheyCloseAnOrganisationsCase()
    {
        var view = RenderStaff(out var cases, [Case("waiting", 5)]);
        var dialogs = Render<RadzenDialog>();

        OpenCase(view, "waiting");
        await view.InvokeAsync(() => view.Find("[data-close]").Click());

        Assert.Contains("better that the organisation closes", dialogs.Find("[data-close-dialog='staff']").TextContent);

        await dialogs.InvokeAsync(() => dialogs.Find("[data-confirm-close]").Click());

        cases.Verify(x => x.CloseCaseAsync(TeamKey, "waiting", default), Times.Once);
    }

    [Fact]
    public void TheStaffQueue_SaysWhoMayReadEachCase()
    {
        var view = RenderStaff(out _, [Case("shared", 5) with { Visibility = SupportCaseVisibility.Team }, Case("own", 4) with { Visibility = SupportCaseVisibility.Private }]);

        Assert.Equal("team", view.Find("[data-case='shared'] [data-readers]").GetAttribute("data-readers"));
        Assert.Contains("Only Alice", view.Find("[data-case='own'] [data-readers='author']").TextContent);
    }

    [Fact]
    public void TheStaffQueue_MarksWhatNeedsAnAnswer_AndListsItFirst()
    {
        var view = RenderStaff(out _, [Case("answered", 90), Case("waiting", 5, awaiting: true)]);

        Assert.Equal(["waiting", "answered"], view.FindAll("[data-case]").Select(x => x.GetAttribute("data-case")).ToArray());
        Assert.Single(view.FindAll("[data-needs-answer]"));
    }

    /// <summary>A queue that stopped at its first page told staff about waiting cases it never listed.</summary>
    [Fact]
    public void TheStaffQueue_ReadsEveryPage()
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 100, default))
            .ReturnsAsync(new SupportCasePage { Items = [Case("first", 10)], NextCursor = "next" });
        cases.Setup(x => x.GetCasesAcrossTeamsAsync("next", 100, default))
            .ReturnsAsync(new SupportCasePage { Items = [Case("second", 5, awaiting: true)] });

        var view = RenderStaff(cases);

        Assert.Equal(["second", "first"], view.FindAll("[data-case]").Select(x => x.GetAttribute("data-case")).ToArray());
    }

    /// <summary>Search and leaving out the kind are asked for by a list; these two lists ask for neither.</summary>
    [Fact]
    public void TheTeamListsKeepTheKindOfConversation_AndOfferNoSearch()
    {
        var staff = RenderStaff(out _, [Case("waiting", 5)]);

        Assert.Empty(staff.FindAll("input[data-search]"));
        Assert.Single(staff.FindComponents<RadzenSelectBar<IEnumerable<SupportAssistance>>>());
    }

    [Fact]
    public void TheMembersList_KeepsTheKindOfConversation_AndOffersNoSearch()
    {
        var view = RenderCases(out _);

        Assert.Empty(view.FindAll("input[data-search]"));
        Assert.Single(view.FindComponents<RadzenSelectBar<IEnumerable<SupportAssistance>>>());
    }

    [Fact]
    public void Staff_AreNotOfferedClose_BeforeTheWindowHasPassed()
    {
        var view = RenderStaff(out _, [Case("waiting", 5)], window: TimeSpan.FromDays(14));

        OpenCase(view, "waiting");

        Assert.Empty(view.FindAll("[data-close]"));
    }

    [Fact]
    public void Staff_AreOfferedClose_WithoutAWindow()
    {
        var view = RenderStaff(out _, [Case("waiting", 5)]);

        OpenCase(view, "waiting");

        Assert.Single(view.FindAll("[data-close]"));
    }

    private static Task ChooseKindsAsync(IRenderedComponent<SupportCasesView> view, params SupportAssistance[] kinds)
    {
        var kind = view.FindComponent<RadzenSelectBar<IEnumerable<SupportAssistance>>>();
        return view.InvokeAsync(() => kind.Instance.Change.InvokeAsync(kinds));
    }

    private static string[] CaseIds<T>(IRenderedComponent<T> view) where T : Microsoft.AspNetCore.Components.IComponent
        => view.FindAll("[data-case]").Select(x => x.GetAttribute("data-case")).ToArray();

    private static void OpenCase<T>(IRenderedComponent<T> view, string caseId) where T : Microsoft.AspNetCore.Components.IComponent
        => view.Find($"[data-case='{caseId}'] [style*='cursor: pointer']").Click();

    private IRenderedComponent<SupportAcrossTeamsView> RenderStaff(out Mock<ISupportCaseService> cases, SupportCase[] items, TimeSpan? window = null)
    {
        cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = items });
        foreach (var item in items)
        {
            cases.Setup(x => x.GetMessagesAsync(TeamKey, item.Id, null, SupportCaseLimits.MaxMessagesPerCase, default))
                .ReturnsAsync(new SupportMessagePage { Items = [new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = "alice", Body = "Help", SentAt = DateTime.UtcNow }] });
        }

        return RenderStaff(cases, window);
    }

    private IRenderedComponent<SupportAcrossTeamsView> RenderStaff(Mock<ISupportCaseService> cases, TimeSpan? window = null)
    {
        Services.AddSingleton(cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        return Render<SupportAcrossTeamsView>(p => p.Add(x => x.StaffCloseAfter, window));
    }

    private IRenderedComponent<SupportCasesView> RenderCases(out Mock<ISupportCaseService> cases, SupportCase[] items = null, string tab = null, Action<string> tabChanged = null)
    {
        items ??= Cases;
        cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 100, default)).ReturnsAsync(new SupportCasePage { Items = items });
        foreach (var item in items)
        {
            cases.Setup(x => x.GetCaseAsync(TeamKey, item.Id, default)).ReturnsAsync(item);
            cases.Setup(x => x.GetVisibilityChoiceAsync(TeamKey, item.Id, default)).ReturnsAsync(new SupportCaseVisibilityChoice(true, true));
            cases.Setup(x => x.GetMessagesAsync(TeamKey, item.Id, null, SupportCaseLimits.MaxMessagesPerCase, default)).ReturnsAsync(new SupportMessagePage { Items = [] });
        }

        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);

        Services.AddSingleton(cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        return Render<SupportCasesView>(p => p.Add(x => x.Tab, tab).Add(x => x.TabChanged, tabChanged ?? (_ => { })));
    }
}
