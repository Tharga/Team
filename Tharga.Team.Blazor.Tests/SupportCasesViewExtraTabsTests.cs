using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A host adds its own tabs to <c>SupportCasesView</c> and they behave like the built-in two.
/// </summary>
/// <remarks>
/// The asks, and why each is a test: a host can add a tab and <c>Tab</c> opens it; a reader the host did not give the tab to sees
/// none; an unknown <c>Tab</c> falls back as it always did; and with no extra tabs nothing about the built-in two changes.
/// </remarks>
public class SupportCasesViewExtraTabsTests : BunitContext
{
    private const string TeamKey = "acme";

    [Fact]
    public void WithNoExtraTabs_OnlyTheBuiltInTwoAreThere()
    {
        var view = RenderView();

        Assert.Equal(["Cases", "Talk to"], Titles(view));
    }

    [Fact]
    public void AnExtraTab_IsDrawnAfterTheBuiltInTwo_WithItsOwnTitle()
    {
        var view = RenderView(extra: [("queue", "Support queue", "the queue body")]);

        Assert.Equal(["Cases", "Talk to", "Support queue"], Titles(view));
    }

    [Fact]
    public void TheTabParameter_OpensAnExtraTab()
    {
        var view = RenderView(tab: "queue", extra: [("queue", "Support queue", "the queue body")]);

        Assert.Equal("Support queue", Selected(view));
    }

    [Fact]
    public async Task TabChanged_ReportsAnExtraTabByName()
    {
        string reported = null;
        var view = RenderView(extra: [("queue", "Support queue", "the queue body")], tabChanged: name => reported = name);

        await view.InvokeAsync(() => view.FindAll(".rz-tabview-nav li").Last().Children[0].Click());

        Assert.Equal("queue", reported);
        Assert.Equal("Support queue", Selected(view));
    }

    /// <summary>A reader the host gave no tab to: the host's own condition leaves it out, so nothing is there to open.</summary>
    [Fact]
    public void ATabTheHostLeavesOut_IsNotThere_AndItsNameFallsBack()
    {
        var view = RenderView(tab: "queue", extra: []);

        Assert.Equal(["Cases", "Talk to"], Titles(view));
        Assert.Equal("Cases", Selected(view));
    }

    [Fact]
    public void AnUnknownTab_FallsBackAsItDidBefore()
    {
        var view = RenderView(tab: "nonsense", extra: [("queue", "Support queue", "the queue body")]);

        Assert.Equal("Cases", Selected(view));
    }

    [Fact]
    public void TheBuiltInTabs_StillOpenByName()
    {
        var view = RenderView(tab: SupportCasesView.NewCaseTab, extra: [("queue", "Support queue", "the queue body")]);

        Assert.Equal("Talk to", Selected(view));
    }

    [Fact]
    public void ATabNamedLikeABuiltInOne_IsRefusedWithAnExplanation()
    {
        var exception = Record.Exception(() => RenderView(extra: [(SupportCasesView.CasesTab, "Mine", "body")]));

        Assert.Contains("clashes", Flatten(exception));
    }

    [Fact]
    public void TwoExtraTabsWithTheSameName_AreRefused()
    {
        var exception = Record.Exception(() => RenderView(extra: [("queue", "One", "a"), ("queue", "Two", "b")]));

        Assert.Contains("clashes", Flatten(exception));
    }

    private static string Flatten(Exception exception) => exception == null ? "" : exception.ToString();

    private static string[] Titles(IRenderedComponent<SupportCasesView> view)
        => view.FindAll(".rz-tabview-nav li .rz-tabview-title").Select(x => x.TextContent.Trim()).ToArray();

    private static string Selected(IRenderedComponent<SupportCasesView> view)
        => view.Find(".rz-tabview-nav li.rz-tabview-selected .rz-tabview-title").TextContent.Trim();

    private IRenderedComponent<SupportCasesView> RenderView(string tab = null, (string Name, string Text, string Body)[] extra = null, Action<string> tabChanged = null)
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 100, default))
            .ReturnsAsync(new SupportCasePage
            {
                Items =
                [
                    new SupportCase { Id = "case-1", TeamKey = TeamKey, AuthorIdentity = "alice", AuthorName = "Alice", Subject = "Export is empty", Status = SupportCaseStatus.Open, CreatedAt = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc), MessageCount = 1 }
                ]
            });

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
        SetRendererInfo(new RendererInfo("Server", true));

        return Render<SupportCasesView>(p =>
        {
            p.Add(x => x.Tab, tab);
            if (extra != null) p.Add(x => x.ExtraTabs, ExtraTabsOf(extra));
            if (tabChanged != null) p.Add(x => x.TabChanged, EventCallback.Factory.Create<string>(this, tabChanged));
        });
    }

    private static RenderFragment ExtraTabsOf((string Name, string Text, string Body)[] extra) => builder =>
    {
        foreach (var (name, text, body) in extra)
        {
            builder.OpenComponent<SupportCasesExtraTab>(0);
            builder.AddAttribute(1, nameof(SupportCasesExtraTab.Name), name);
            builder.AddAttribute(2, nameof(SupportCasesExtraTab.Text), text);
            builder.AddAttribute(3, nameof(SupportCasesExtraTab.ChildContent), (RenderFragment)(inner => inner.AddContent(0, body)));
            builder.CloseComponent();
        }
    };
}
