using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Which member of support staff a case is assigned to: shown on the card, changed inside the opened case, and filtered on in the list.
/// </summary>
/// <remarks>
/// <b>Rendered, and the control clicked</b>, for the reason the sibling views taught: a handler Blazor swallows as an unmatched attribute compiles
/// and passes every test that does not click it.
/// </remarks>
public class SupportAssigneeRenderTests : BunitContext
{
    private const string AcrossStorageKey = "Tharga.Team.Support.AcrossTeams.Filter";

    private static readonly SupportCaseAssignee Sam = new() { Key = "sam", Name = "Sam Support", Email = "sam@example.com" };
    private static readonly SupportCaseAssignee Olivia = new() { Key = "olivia", Name = "Olivia Operator" };

    private static readonly SupportCase Mine = Case("mine", "acme", "Export is empty") with { Assignee = Sam };
    private static readonly SupportCase Hers = Case("hers", "globex", "Cannot invite") with { Assignee = Olivia };
    private static readonly SupportCase Nobodys = Case("nobodys", "acme", "Invoice is missing");

    private static readonly SupportMessage Asked = new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Body = "The export file has no rows.",
        SentAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc)
    };

    private static SupportCase Case(string id, string teamKey, string subject) => new()
    {
        Id = id,
        TeamKey = teamKey,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = subject,
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    [Fact]
    public void ACardNamesWhoHasTheCase_AndACaseNobodyHasNamesNobody()
    {
        var view = Across(out _, [Mine, Nobodys]);

        Assert.Contains("Sam Support", view.Find("[data-case='mine'] [data-assignee='mine']").TextContent);
        Assert.Equal("sam@example.com", view.Find("[data-assignee='mine']").GetAttribute("title"));
        Assert.Empty(view.FindAll("[data-case='nobodys'] [data-assignee]"));
    }

    /// <summary>The card header shows the assignee as a picture, so the list says who has each case at a glance (#526).</summary>
    [Fact]
    public void ACardShowsTheAssigneesAvatar_NotAGenericIcon()
    {
        var view = Across(out _, [Mine]);

        var assignee = view.Find("[data-case='mine'] [data-assignee='mine']");

        Assert.Single(view.FindComponents<UserAvatar>());
        Assert.DoesNotContain("person", assignee.TextContent);
    }

    /// <summary>The caption sits on its own line above the dropdown, as a form field label (#526).</summary>
    [Fact]
    public void ThePickerCaption_IsAboveTheDropdown_NotBesideIt()
    {
        var view = Across(out _, [Mine]);
        Open(view, "mine");

        var picker = view.Find("[data-assignee-picker='mine']");
        var rows = picker.QuerySelectorAll(".rz-stack").ToArray();

        Assert.DoesNotContain(rows, x => x.ClassList.Contains("rz-flex-row") && x.QuerySelector(".rz-dropdown") != null);
        Assert.True(picker.InnerHtml.IndexOf("Assigned to", StringComparison.Ordinal) < picker.InnerHtml.IndexOf("rz-dropdown", StringComparison.Ordinal));
    }

    /// <summary>In the opened case the picker is the first thing and sits at the end of the line, so it does not move when the host's extras change height (#526).</summary>
    [Fact]
    public void TheOpenedCase_PlacesThePickerFirst_AtTheEnd()
    {
        var view = Across(out _, [Mine]);
        Open(view, "mine");

        var conversation = view.Find("[data-conversation='mine']");
        var picker = conversation.QuerySelector("[data-assignee-picker='mine']");

        Assert.Same(conversation.FirstElementChild, picker);
        Assert.Contains("align-self: flex-end", picker.GetAttribute("style"));
    }

    [Fact]
    public void TheUnassignedQueue_PlacesThePickerFirst_AtTheEnd()
    {
        var view = Unassigned(out _, [Case("loose", null, "Cannot sign in") with { Assignee = Sam }]);
        Open(view, "loose");

        var conversation = view.Find("[data-conversation='loose']");
        var picker = conversation.QuerySelector("[data-assignee-picker='loose']");

        Assert.Same(conversation.FirstElementChild, picker);
        Assert.Contains("align-self: flex-end", picker.GetAttribute("style"));
    }

    [Fact]
    public void WithAssigneesTurnedOff_NothingAboutThemIsShown()
    {
        var view = Across(out _, [Mine, Nobodys], showAssignee: false);

        Open(view, "mine");

        Assert.Empty(view.FindAll("[data-assignee]"));
        Assert.Empty(view.FindAll("[data-assignee-picker]"));
        Assert.Empty(view.FindAll("[data-assignee-filter]"));
    }

    [Fact]
    public void OpeningACase_OffersThePicker_ShowingWhoHasIt()
    {
        var view = Across(out _, [Mine]);

        Open(view, "mine");

        var picker = view.FindComponent<SupportAssigneePicker>();
        Assert.Equal("sam", picker.FindComponent<RadzenDropDown<string>>().Instance.Value);
    }

    [Fact]
    public async Task ChoosingAPerson_AssignsTheCaseToThemInItsOwnTeam()
    {
        var view = Across(out var cases, [Nobodys]);
        Open(view, "nobodys");

        await ChooseAsync(view, "olivia");

        cases.Verify(x => x.SetCaseAssigneeAsync("acme", "nobodys", "olivia", default), Times.Once);
    }

    [Fact]
    public async Task ClearingThePicker_TakesTheAssigneeAway()
    {
        var view = Across(out var cases, [Mine]);
        Open(view, "mine");

        await ChooseAsync(view, null);

        cases.Verify(x => x.SetCaseAssigneeAsync("acme", "mine", null, default), Times.Once);
    }

    [Fact]
    public async Task AnAssignment_TellsTheListToReadTheCaseAgain()
    {
        var view = Across(out var cases, [Nobodys]);
        Open(view, "nobodys");
        cases.Invocations.Clear();

        await ChooseAsync(view, "olivia");

        cases.Verify(x => x.GetCasesAcrossTeamsAsync(null, 100, default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task APersonTheServiceRefuses_IsSaidSo()
    {
        var view = Across(out var cases, [Nobodys]);
        Open(view, "nobodys");
        cases.Setup(x => x.SetCaseAssigneeAsync("acme", "nobodys", "olivia", default)).ThrowsAsync(new ArgumentException("not staff"));

        await ChooseAsync(view, "olivia");

        Assert.Contains("could not be assigned", view.Find("[data-assignee-failed='nobodys']").TextContent);
    }

    /// <summary>A person who left the staff still holds cases, and the picker must say so rather than show nobody.</summary>
    [Fact]
    public void ACaseHeldByAPersonNoLongerOffered_StillShowsThem()
    {
        var left = new SupportCaseAssignee { Key = "gone", Name = "Gone Gary" };
        var view = Across(out _, [Nobodys with { Assignee = left }]);

        Open(view, "nobodys");

        Assert.Equal("gone", view.FindComponent<SupportAssigneePicker>().FindComponent<RadzenDropDown<string>>().Instance.Value);
    }

    [Fact]
    public void WhereTheServiceCannotAssign_NeitherThePickerNorTheFilterIsOffered()
    {
        var view = Across(out _, [Mine], arrange: cases => cases.Setup(x => x.GetAssignableStaffAsync(default)).ThrowsAsync(new NotSupportedException()));

        Open(view, "mine");

        Assert.Empty(view.FindAll("[data-assignee-picker]"));
        Assert.Empty(view.FindAll("[data-assignee-filter]"));
        Assert.NotEmpty(view.FindAll("[data-assignee='mine']"));
    }

    [Fact]
    public void ForSomebodyWithoutAStaffGrant_NeitherThePickerNorTheFilterIsOffered()
    {
        var view = Across(out _, [Mine], arrange: cases => cases.Setup(x => x.GetAssignableStaffAsync(default)).ThrowsAsync(new UnauthorizedAccessException()));

        Open(view, "mine");

        Assert.Empty(view.FindAll("[data-assignee-picker]"));
        Assert.Empty(view.FindAll("[data-assignee-filter]"));
    }

    [Fact]
    public void ByDefault_EveryCaseIsShown_WhoeverHasIt()
    {
        var view = Across(out _, [Mine, Hers, Nobodys]);

        Assert.Equal(3, CaseIds(view).Length);
    }

    [Fact]
    public async Task FilteringOnNobody_ShowsOnlyTheCasesNobodyHas()
    {
        var view = Across(out _, [Mine, Hers, Nobodys]);

        await FilterAsync(view, "none");

        Assert.Equal(["nobodys"], CaseIds(view));
    }

    [Fact]
    public async Task FilteringOnAPerson_ShowsOnlyTheirCases()
    {
        var view = Across(out _, [Mine, Hers, Nobodys]);

        await FilterAsync(view, "key:olivia");

        Assert.Equal(["hers"], CaseIds(view));
    }

    [Fact]
    public async Task FilteringOnAnyone_BringsEveryCaseBack()
    {
        var view = Across(out _, [Mine, Hers, Nobodys]);
        await FilterAsync(view, "none");

        await FilterAsync(view, "any");

        Assert.Equal(3, CaseIds(view).Length);
    }

    [Fact]
    public void TheFilterOffersEveryoneTheServiceOffers_AndWhoeverAlreadyHoldsACase()
    {
        var left = new SupportCaseAssignee { Key = "gone", Name = "Gone Gary" };
        var view = Across(out _, [Nobodys with { Assignee = left }]);

        var options = view.FindComponent<SupportCaseFilter>().FindComponent<RadzenDropDown<string>>().Instance.Data.Cast<object>().Select(x => x.ToString()).ToArray();

        Assert.Contains(options, x => x.Contains("Gone Gary"));
        Assert.Contains(options, x => x.Contains("Sam Support"));
        Assert.Contains(options, x => x.Contains("Olivia Operator"));
    }

    [Fact]
    public async Task TheChoice_IsRemembered_UnderTheViewsOwnKey()
    {
        var view = Across(out _, [Mine, Hers]);

        await FilterAsync(view, "key:sam");

        var stored = JSInterop.VerifyInvoke("localStorage.setItem").Arguments;
        Assert.Equal(AcrossStorageKey, stored[0]);
        Assert.Contains("key:sam", stored[1]?.ToString());
    }

    [Fact]
    public void ARememberedChoice_IsRestored()
    {
        JSInterop.Setup<string>("localStorage.getItem", x => (string)x.Arguments[0] == AcrossStorageKey)
            .SetResult("{\"Statuses\":[],\"Kinds\":[],\"Assignee\":\"none\"}");

        var view = Across(out _, [Mine, Hers, Nobodys]);

        view.WaitForAssertion(() => Assert.Equal(["nobodys"], CaseIds(view)));
    }

    /// <summary>A choice remembered before assignees existed has none, and shows every case as it always did.</summary>
    [Fact]
    public void AChoiceRememberedBeforeAssigneesExisted_ShowsEveryCase()
    {
        JSInterop.Setup<string>("localStorage.getItem", x => (string)x.Arguments[0] == AcrossStorageKey)
            .SetResult("{\"Statuses\":[],\"Kinds\":[]}");

        var view = Across(out _, [Mine, Hers, Nobodys]);

        view.WaitForAssertion(() => Assert.Equal(3, CaseIds(view).Length));
    }

    [Fact]
    public async Task APerson_IsStillFilteredOn_WhenTheyAreNoLongerOffered()
    {
        JSInterop.Setup<string>("localStorage.getItem", x => (string)x.Arguments[0] == AcrossStorageKey)
            .SetResult("{\"Statuses\":[],\"Kinds\":[],\"Assignee\":\"key:retired\"}");
        var retired = new SupportCaseAssignee { Key = "retired", Name = "Retired Rita" };
        var view = Across(out _, [Mine, Nobodys with { Assignee = retired }]);

        await view.InvokeAsync(() => { });

        view.WaitForAssertion(() => Assert.Equal(["nobodys"], CaseIds(view)));
    }

    [Fact]
    public void Searching_FindsTheAssignee_ByNameAndAddress()
    {
        var view = Unassigned(out _, [Mine with { TeamKey = null }, Hers with { TeamKey = null }, Nobodys with { TeamKey = null }]);

        view.Find("input[data-search]").Input("sam@example");

        Assert.Equal(["mine"], CaseIds(view));
    }

    [Fact]
    public async Task TheUnassignedQueue_ShowsWhoHasACase_AndOffersThePickerWithNoTeam()
    {
        var unassigned = Case("loose", null, "Cannot sign in") with { Assignee = Sam };
        var view = Unassigned(out var cases, [unassigned]);

        Assert.Contains("Sam Support", view.Find("[data-assignee='loose']").TextContent);

        view.Find("[data-case='loose'] [style*='cursor: pointer']").Click();
        var dropdown = view.FindComponent<SupportAssigneePicker>().FindComponent<RadzenDropDown<string>>();
        await view.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync("olivia"));

        cases.Verify(x => x.SetCaseAssigneeAsync(null, "loose", "olivia", default), Times.Once);
    }

    [Fact]
    public void TheUnassignedQueue_LeavesAssigneesOut_WhenTurnedOff()
    {
        var unassigned = Case("loose", null, "Cannot sign in") with { Assignee = Sam };
        var view = Unassigned(out _, [unassigned], showAssignee: false);

        view.Find("[data-case='loose'] [style*='cursor: pointer']").Click();

        Assert.Empty(view.FindAll("[data-assignee]"));
        Assert.Empty(view.FindAll("[data-assignee-picker]"));
        Assert.Empty(view.FindAll("[data-assignee-filter]"));
    }

    private static string[] CaseIds<T>(IRenderedComponent<T> view) where T : Microsoft.AspNetCore.Components.IComponent
        => view.FindAll("[data-case]").Select(x => x.GetAttribute("data-case")).Order().ToArray();

    private static void Open<T>(IRenderedComponent<T> view, string caseId) where T : Microsoft.AspNetCore.Components.IComponent
        => view.Find($"[data-case='{caseId}'] [style*='cursor: pointer']").Click();

    private static Task ChooseAsync(IRenderedComponent<SupportAcrossTeamsView> view, string key)
    {
        var dropdown = view.FindComponent<SupportAssigneePicker>().FindComponent<RadzenDropDown<string>>();
        return view.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync(key));
    }

    private static Task FilterAsync(IRenderedComponent<SupportAcrossTeamsView> view, string choice)
    {
        var dropdown = view.FindComponent<SupportCaseFilter>().FindComponent<RadzenDropDown<string>>();
        return view.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync(choice));
    }

    private Mock<ISupportCaseService> Service(SupportCase[] items, Action<Mock<ISupportCaseService>> arrange)
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = items });
        cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = items });
        cases.Setup(x => x.GetAwaitingSupportCountAcrossTeamsAsync(default)).ReturnsAsync(0);
        cases.Setup(x => x.GetAwaitingUnassignedCountAsync(default)).ReturnsAsync(0);
        cases.Setup(x => x.GetAssignableStaffAsync(default)).ReturnsAsync([Olivia, Sam]);
        foreach (var item in items)
        {
            cases.Setup(x => x.GetMessagesAsync(item.TeamKey, item.Id, null, SupportCaseLimits.MaxMessagesPerCase, default)).ReturnsAsync(new SupportMessagePage { Items = [Asked] });
        }

        arrange?.Invoke(cases);

        return cases;
    }

    private void Register(Mock<ISupportCaseService> cases)
    {
        Services.AddSingleton(cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.TryAddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();

        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));
    }

    private IRenderedComponent<SupportAcrossTeamsView> Across(
        out Mock<ISupportCaseService> cases, SupportCase[] items, bool showAssignee = true, Action<Mock<ISupportCaseService>> arrange = null)
    {
        cases = Service(items, arrange);
        Register(cases);

        return Render<SupportAcrossTeamsView>(p => p.Add(x => x.ShowAssignee, showAssignee));
    }

    private IRenderedComponent<SupportUnassignedView> Unassigned(out Mock<ISupportCaseService> cases, SupportCase[] items, bool showAssignee = true)
    {
        cases = Service(items, null);
        Register(cases);

        return Render<SupportUnassignedView>(p => p.Add(x => x.ShowAssignee, showAssignee));
    }
}
