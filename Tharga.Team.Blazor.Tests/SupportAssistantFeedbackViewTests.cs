using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// How the assistant is doing: what it was asked and could not do, and how its answers were rated, listed for the people
/// who build it and marked on the desk.
/// </summary>
public class SupportAssistantFeedbackViewTests : BunitContext
{
    private const string BySubject = "search cases by subject";

    private readonly Mock<ISupportCaseService> _cases = new();

    private static SupportCase Case(string id, string teamKey, DateTime at, params string[] gaps) => new()
    {
        Id = id,
        TeamKey = teamKey,
        TeamName = teamKey.ToUpperInvariant(),
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = $"Subject of {id}",
        Status = SupportCaseStatus.Open,
        CreatedAt = at,
        LastMessageAt = at,
        MessageCount = 1,
        AssistantState = SupportAssistantState.Active,
        AssistantGaps = gaps
    };

    private static readonly DateTime Monday = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheSameThingAskedInSeveralCases_IsOneEntry_MostAskedFirst()
    {
        var gaps = SupportAssistantFeedbackView.GapsOf(
        [
            Case("c1", "acme", Monday, BySubject),
            Case("c2", "globex", Monday.AddDays(1), "Search Cases By Subject", "close my case"),
            Case("c3", "acme", Monday.AddDays(2))
        ]);

        Assert.Equal([BySubject, "close my case"], gaps.Select(x => x.Text));
        Assert.Equal(["c2", "c1"], gaps[0].Cases.Select(x => x.Id));
        Assert.Equal(Monday.AddDays(1), gaps[0].Last);
    }

    [Fact]
    public void TheList_ShowsEachThing_WithHowManyCasesAskedForIt()
    {
        var view = Render([Case("c1", "acme", Monday, BySubject), Case("c2", "globex", Monday, BySubject)]);

        Assert.Equal("2", view.Find($"[data-gap='{BySubject}'] [data-cases]").GetAttribute("data-cases"));
        Assert.Empty(view.FindAll("[data-feedback-case]"));

        view.Find($"[data-gap='{BySubject}'] [style*='cursor: pointer']").Click();

        Assert.Equal(["c1", "c2"], view.FindAll("[data-feedback-case]").Select(x => x.GetAttribute("data-feedback-case")).Order());
    }

    [Fact]
    public void WithNothingTheAssistantCouldNotDo_TheListSaysSo()
    {
        var view = Render([Case("c1", "acme", Monday)]);

        Assert.Equal(SupportAssistantFeedbackViewText.NoGaps.Default, view.Find("[data-no-gaps]").TextContent);
    }

    [Fact]
    public void AReaderWhoMayNotReadAcrossTeams_IsToldSo()
    {
        _cases.Setup(x => x.GetCasesAcrossTeamsAsync(It.IsAny<string>(), It.IsAny<int>(), default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = Render(null);

        Assert.Contains(SupportAssistantFeedbackViewText.NotPermitted.Default, view.Markup);
    }

    [Fact]
    public void RatedAnswers_AreListedByHowTheyWereRated_NewestFirst()
    {
        var view = Render(
        [
            Case("c1", "acme", Monday) with { AnswersRatedDown = 2 },
            Case("c2", "acme", Monday.AddDays(1)) with { AnswersRatedDown = 1, AnswersRatedUp = 1 },
            Case("c3", "acme", Monday.AddDays(2)) with { AnswersRatedUp = 3 }
        ]);

        Assert.Equal(["c2", "c1"], view.FindAll("[data-feedback='down'] [data-feedback-case]").Select(x => x.GetAttribute("data-feedback-case")));
        Assert.Equal(["c3", "c2"], view.FindAll("[data-feedback='up'] [data-feedback-case]").Select(x => x.GetAttribute("data-feedback-case")));
        Assert.Equal("2", view.Find("[data-feedback='down'] [data-feedback-case='c1'] [data-answers]").GetAttribute("data-answers"));
    }

    [Fact]
    public void WithNothingRated_EachRatingSaysSo()
    {
        var view = Render([Case("c1", "acme", Monday)]);

        Assert.Equal(2, view.FindAll("[data-none-rated]").Count);
    }

    [Fact]
    public void TheDesk_MarksACaseWithARatedAnswer()
    {
        _cases.Setup(x => x.GetCasesAcrossTeamsAsync(It.IsAny<string>(), It.IsAny<int>(), default))
            .ReturnsAsync(new SupportCasePage { Items = [Case("c1", "acme", Monday) with { AnswersRatedDown = 1, AssistantState = SupportAssistantState.HandedOff }, Case("c2", "acme", Monday) with { AnswersRatedUp = 1, AssistantState = SupportAssistantState.HandedOff }] });
        AddServices();

        var view = Render<SupportAcrossTeamsView>();

        Assert.NotNull(view.Find("[data-rated-down='c1']"));
        Assert.Empty(view.FindAll("[data-rated-up='c1']"));
        Assert.NotNull(view.Find("[data-rated-up='c2']"));
    }

    [Fact]
    public void TheDesk_MarksACaseTheAssistantCouldNotHelpWith()
    {
        _cases.Setup(x => x.GetCasesAcrossTeamsAsync(It.IsAny<string>(), It.IsAny<int>(), default))
            .ReturnsAsync(new SupportCasePage { Items = [Case("c1", "acme", Monday, BySubject, "close my case") with { AssistantState = SupportAssistantState.HandedOff }] });
        AddServices();

        var view = Render<SupportAcrossTeamsView>();

        var badge = view.Find("[data-assistant-gaps='c1']");
        Assert.Equal($"{BySubject}; close my case", badge.GetAttribute("title"));
    }

    private IRenderedComponent<SupportAssistantFeedbackView> Render(SupportCase[] cases)
    {
        if (cases != null)
            _cases.Setup(x => x.GetCasesAcrossTeamsAsync(It.IsAny<string>(), It.IsAny<int>(), default)).ReturnsAsync(new SupportCasePage { Items = cases });
        AddServices();

        return Render<SupportAssistantFeedbackView>();
    }

    private void AddServices()
    {
        Services.AddSingleton(_cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.TryAddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));
    }
}
