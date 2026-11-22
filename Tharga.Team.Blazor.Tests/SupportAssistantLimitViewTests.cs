using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What the platform's staff let each team spend on the assistant: the default and each team's own limit, shown and changed, only to a caller holding the system
/// scope, and as a tab on the staff page only then.
/// </summary>
public class SupportAssistantLimitViewTests : BunitContext
{
    private readonly Mock<ISupportAssistantLimitService> _limits = new();

    private static SupportAssistantLimits Limits(decimal? defaultLimit = 10m, bool stored = false, params SupportAssistantLimit[] teams) => new()
    {
        DefaultLimit = defaultLimit,
        StoredDefault = stored,
        OptionDefault = 10m,
        Currency = "USD",
        Teams = teams
    };

    private static SupportAssistantLimit Own(string key, decimal? limit) => new() { TeamKey = key, Limit = limit, IsOwn = true, DefaultLimit = 10m, Currency = "USD" };

    private static ITeam Team(string key, string name)
    {
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(key);
        team.SetupGet(x => x.Name).Returns(name);
        return team.Object;
    }

    private static async IAsyncEnumerable<ITeam> Teams(params ITeam[] teams)
    {
        foreach (var team in teams)
        {
            await Task.Yield();
            yield return team;
        }
    }

    private void Prepare(params ITeam[] teams)
    {
        var directory = new Mock<ITeamDirectoryService>();
        directory.Setup(x => x.GetTeamsAsync()).Returns(() => Teams(teams));
        Services.AddSingleton(directory.Object);
        Services.AddSingleton(_limits.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>The teams are read as an async stream, so the view is loaded a moment after it is rendered.</summary>
    private IRenderedComponent<SupportAssistantLimitView> RenderView(params ITeam[] teams)
    {
        Prepare(teams);
        var view = Render<SupportAssistantLimitView>();
        view.WaitForAssertion(() => Assert.True(view.FindAll("[data-default-limit]").Count > 0 || view.Markup.Contains(SupportAssistantLimitViewText.NotPermitted.Default)));

        // The view is drawn before the directory has finished streaming, and the team picker appears with it; a click made before then lands on a view that is
        // about to be redrawn, and what it showed is gone (this failed once on the build server, and not on any machine here).
        if (teams.Length > 0 && !view.Markup.Contains(SupportAssistantLimitViewText.NotPermitted.Default)) view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-team-choice]")));
        return view;
    }

    [Fact]
    public void TheDefault_SaysWhatHolds_AndThatItIsTheHostsUntilStaffSetOne()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m));

        var view = RenderView();

        var now = view.Find("[data-default-now]").TextContent;
        Assert.Contains($"{10m:N2} USD", now);
        Assert.Contains(SupportAssistantLimitViewText.FromHost.Default, now);
        Assert.Empty(view.FindAll("[data-clear-default]"));
    }

    [Fact]
    public void ADefaultStaffSet_SaysSo_AndCanBeGivenBackToTheHost()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(3m, stored: true));
        _limits.Setup(x => x.ClearDefaultLimitAsync(default)).ReturnsAsync(Limits(10m));

        var view = RenderView();

        Assert.Contains(SupportAssistantLimitViewText.FromHere.Default, view.Find("[data-default-now]").TextContent);
        view.Find("[data-clear-default]").Click();

        view.WaitForAssertion(() => _limits.Verify(x => x.ClearDefaultLimitAsync(default), Times.Once));
    }

    [Fact]
    public void ADefaultWithNoLimit_IsSaidInWords_NotAsAZero()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(null, stored: true));

        Assert.Contains(SupportAssistantLimitViewText.NoLimit.Default, RenderView().Find("[data-default-now]").TextContent);
    }

    [Fact]
    public void SavingTheDefault_SendsTheAmountShown()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m));
        _limits.Setup(x => x.SetDefaultLimitAsync(It.IsAny<SupportAssistantDefaultLimitRequest>(), default)).ReturnsAsync(Limits(10m, stored: true));

        var view = RenderView();
        view.Find("[data-save-default]").Click();

        view.WaitForAssertion(() => _limits.Verify(x => x.SetDefaultLimitAsync(It.Is<SupportAssistantDefaultLimitRequest>(r => r.Limit == 10m), default), Times.Once));
    }

    [Fact]
    public void ADefaultWithNoAmount_AndNotChosenAsNoLimit_IsNotSent()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(null));

        var view = RenderView();
        view.Find("[data-save-default]").Click();

        Assert.Contains(SupportAssistantLimitViewText.EnterAmount.Default, view.Find("[data-problem]").TextContent);
        _limits.Verify(x => x.SetDefaultLimitAsync(It.IsAny<SupportAssistantDefaultLimitRequest>(), default), Times.Never);
    }

    [Fact]
    public void TheTeamsWithALimitOfTheirOwn_AreListed_ByNameWithTheirAmount()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m, false, Own("acme", 50m), Own("globex", null)));

        var view = RenderView(Team("acme", "Acme Inc"), Team("globex", "Globex"));

        var rows = view.FindAll("[data-team-limit-row]");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Acme Inc", view.Find("[data-team-limit-row='acme'] [data-team]").TextContent);
        Assert.Contains($"{50m:N2} USD", view.Find("[data-team-limit-row='acme'] [data-limit]").TextContent);
        Assert.Contains(SupportAssistantLimitViewText.NoLimit.Default, view.Find("[data-team-limit-row='globex'] [data-limit]").TextContent);
    }

    [Fact]
    public void ATeamTheDirectoryDoesNotKnow_IsListedByItsKey()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m, false, Own("gone", 5m)));

        Assert.Equal("gone", RenderView().Find("[data-team-limit-row='gone'] [data-team]").TextContent);
    }

    [Fact]
    public void WithNoTeamLimits_ItSaysAllAreHeldToTheDefault()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits());

        Assert.NotNull(RenderView().Find("[data-no-team-limits]"));
    }

    [Fact]
    public void ATeamLimit_IsEdited_AndSavedForThatTeam()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m, false, Own("acme", 50m)));
        _limits.Setup(x => x.SetLimitAsync(It.IsAny<SupportAssistantLimitRequest>(), default)).ReturnsAsync(Own("acme", 50m));

        var view = RenderView(Team("acme", "Acme Inc"));
        view.Find("[data-edit-team-limit]").Click();
        view.Find("[data-save-team-limit]").Click();

        view.WaitForAssertion(() => _limits.Verify(x => x.SetLimitAsync(It.Is<SupportAssistantLimitRequest>(r => r.TeamKey == "acme" && r.Limit == 50m), default), Times.Once));
    }

    [Fact]
    public void ATeamLimitOfNone_IsKeptAsNone_WhenEdited()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m, false, Own("acme", null)));
        _limits.Setup(x => x.SetLimitAsync(It.IsAny<SupportAssistantLimitRequest>(), default)).ReturnsAsync(Own("acme", null));

        var view = RenderView(Team("acme", "Acme Inc"));
        view.Find("[data-edit-team-limit]").Click();
        view.Find("[data-save-team-limit]").Click();

        view.WaitForAssertion(() => _limits.Verify(x => x.SetLimitAsync(It.Is<SupportAssistantLimitRequest>(r => r.TeamKey == "acme" && r.Limit == null), default), Times.Once));
    }

    [Fact]
    public void UsingTheDefaultAgain_RemovesTheTeamsOwnLimit()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m, false, Own("acme", 50m)));
        _limits.Setup(x => x.ClearLimitAsync("acme", default)).ReturnsAsync(Own("acme", null) with { IsOwn = false });

        var view = RenderView(Team("acme", "Acme Inc"));
        view.Find("[data-clear-team-limit]").Click();

        view.WaitForAssertion(() => _limits.Verify(x => x.ClearLimitAsync("acme", default), Times.Once));
    }

    [Fact]
    public void NoTeamChosen_IsNotSent()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits());

        var view = RenderView(Team("acme", "Acme Inc"));
        view.Find("[data-save-team-limit]").Click();

        Assert.Contains(SupportAssistantLimitViewText.ChooseTeam.Default, view.Find("[data-problem]").TextContent);
        _limits.Verify(x => x.SetLimitAsync(It.IsAny<SupportAssistantLimitRequest>(), default), Times.Never);
    }

    [Fact]
    public void WhereTheDirectoryCannotBeRead_AKeyIsTyped()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits());

        var view = RenderView();

        Assert.NotNull(view.Find("[data-team-key]"));
        Assert.Empty(view.FindAll("[data-team-choice]"));
    }

    [Fact]
    public void ACallerWithoutTheScope_IsToldSo_AndSeesNothingToChange()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = RenderView();

        Assert.Contains(SupportAssistantLimitViewText.NotPermitted.Default, view.Markup);
        Assert.Empty(view.FindAll("[data-save-default]"));
        Assert.Empty(view.FindAll("[data-save-team-limit]"));
    }

    [Fact]
    public void AScopeLostWhileSaving_HidesTheControls()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m));
        _limits.Setup(x => x.SetDefaultLimitAsync(It.IsAny<SupportAssistantDefaultLimitRequest>(), default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = RenderView();
        view.Find("[data-save-default]").Click();

        Assert.Contains(SupportAssistantLimitViewText.NotPermitted.Default, view.Markup);
    }

    [Fact]
    public void ARefusalOfTheAmount_IsShown()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits(10m));
        _limits.Setup(x => x.SetDefaultLimitAsync(It.IsAny<SupportAssistantDefaultLimitRequest>(), default)).ThrowsAsync(new ArgumentOutOfRangeException("limit", "A limit cannot be negative."));

        var view = RenderView();
        view.Find("[data-save-default]").Click();

        Assert.Contains("A limit cannot be negative.", view.Find("[data-problem]").TextContent);
    }

    // ---- the tab ----

    private IRenderedComponent<SupportAssistantAdminView> RenderAdmin(bool registered = true)
    {
        var usage = new Mock<ISupportAssistantUsageService>();
        usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync([]);
        Services.AddSingleton(usage.Object);
        if (registered) Services.AddSingleton(_limits.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportAssistantAdminView>();
    }

    [Fact]
    public void ACallerWithTheScope_SeesTheLimitsTab()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ReturnsAsync(Limits());

        Assert.Contains(SupportAssistantAdminViewText.LimitsTab.Default, RenderAdmin().Markup);
    }

    [Fact]
    public void ACallerWithoutTheScope_SeesNoLimitsTab()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = RenderAdmin();

        Assert.Contains(SupportAssistantAdminViewText.UsageTab.Default, view.Markup);
        Assert.DoesNotContain(SupportAssistantAdminViewText.LimitsTab.Default, view.Markup);
    }

    [Fact]
    public void WhereNoLimitStoreIsRegistered_ThereIsNoLimitsTab_AndNoFailure()
    {
        _limits.Setup(x => x.GetLimitsAsync(default)).ThrowsAsync(new InvalidOperationException("No store."));

        Assert.DoesNotContain(SupportAssistantAdminViewText.LimitsTab.Default, RenderAdmin().Markup);
    }

    [Fact]
    public void WhereTheServiceIsNotRegistered_ThereIsNoLimitsTab()
        => Assert.DoesNotContain(SupportAssistantAdminViewText.LimitsTab.Default, RenderAdmin(registered: false).Markup);
}
