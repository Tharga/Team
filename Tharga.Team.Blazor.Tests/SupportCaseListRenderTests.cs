using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The case lists show what is going on: each case says what kind of conversation it is and when it last moved,
/// the liveliest first, with closed cases and assistant conversations out of the way until asked for.
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
    public void YourCases_HideClosedAndAssistantConversations_AndPutTheLiveliestFirst()
    {
        var view = RenderCases(out _);

        Assert.Equal(["newest", "older"], view.FindAll("[data-case]").Select(x => x.GetAttribute("data-case")).ToArray());
    }

    [Fact]
    public void ShowingClosed_BringsThemBack()
    {
        var view = RenderCases(out _);

        view.Find("input[name='showClosed']").Change(true);

        Assert.Contains(view.FindAll("[data-case]"), x => x.GetAttribute("data-case") == "closed");
    }

    [Fact]
    public void EachCaseSaysWhatKindOfConversationItIs_BeforeItIsOpened()
    {
        var view = RenderCases(out _);

        Assert.All(view.FindAll("[data-kind]"), x => Assert.Equal("support", x.GetAttribute("data-kind")));
        Assert.DoesNotContain("The opening line.", view.Markup);
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
    public async Task TheAuthorClosesTheirOwnCase()
    {
        var view = RenderCases(out var cases);

        var panel = view.FindComponents<RadzenPanel>().First(x => x.Markup.Contains("Subject newest"));
        await view.InvokeAsync(() => panel.Instance.Expand.InvokeAsync());
        view.FindAll("button").First(b => b.TextContent.Contains("Close case")).Click();

        cases.Verify(x => x.CloseCaseAsync(TeamKey, "newest", default), Times.Once);
    }

    [Fact]
    public void TheStaffQueue_MarksWhatNeedsAnAnswer_AndListsItFirst()
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 20, default))
            .ReturnsAsync(new SupportCasePage { Items = [Case("answered", 90), Case("waiting", 5, awaiting: true)] });

        Services.AddSingleton(cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<SupportAcrossTeamsView>();

        Assert.Equal(["waiting", "answered"], view.FindAll("[data-case]").Select(x => x.GetAttribute("data-case")).ToArray());
        Assert.Single(view.FindAll("[data-needs-answer]"));
    }

    private IRenderedComponent<SupportCasesView> RenderCases(out Mock<ISupportCaseService> cases)
    {
        cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 20, default)).ReturnsAsync(new SupportCasePage { Items = Cases });
        foreach (var item in Cases)
        {
            cases.Setup(x => x.GetCaseAsync(TeamKey, item.Id, default)).ReturnsAsync(item);
            cases.Setup(x => x.GetMessagesAsync(TeamKey, item.Id, null, 50, default)).ReturnsAsync(new SupportMessagePage { Items = [] });
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
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportCasesView>();
    }
}
