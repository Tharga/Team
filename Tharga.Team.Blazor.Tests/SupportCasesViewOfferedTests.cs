using Blazored.LocalStorage;
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
/// Who may be talked to when help is asked for: the assistant and a person as one choice on a select bar, limited to what
/// the host offers and to whether the team is ready for the assistant, and no control at all when only one way is left.
/// </summary>
public class SupportCasesViewOfferedTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string Help = "The export is empty";

    private static readonly SupportCase Raised = new()
    {
        Id = "case-1",
        TeamKey = TeamKey,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = Help,
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    private readonly Mock<ISupportCaseService> _cases = new();

    private IRenderedComponent<SupportCasesView> RenderView(SupportAssistantReadiness readiness, SupportAssistance[] offered = null, IThargaTextProvider texts = null)
    {
        _cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 100, default)).ReturnsAsync(new SupportCasePage { Items = [] });
        _cases.Setup(x => x.RaiseCaseAsync(TeamKey, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SupportAssistance>(), default)).ReturnsAsync(Raised);
        _cases.Setup(x => x.GetCaseAsync(TeamKey, Raised.Id, default)).ReturnsAsync(Raised);
        _cases.Setup(x => x.GetAssistantReadinessAsync(TeamKey, default)).ReturnsAsync(readiness);

        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);

        Services.AddSingleton(_cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton(texts ?? new DefaultThargaTextProvider());
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();

        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        return Render<SupportCasesView>(p => p.Add(x => x.Offered, offered));
    }

    private static IReadOnlyList<IRenderedComponent<RadzenSelectBar<SupportAssistance>>> Choices(IRenderedComponent<SupportCasesView> view)
        => view.FindComponents<RadzenSelectBar<SupportAssistance>>();

    private static void Send(IRenderedComponent<SupportCasesView> view)
    {
        view.Find("textarea").Input(Help);
        view.FindAll("button").Single(x => x.TextContent.Contains("Send")).Click();
    }

    [Fact]
    public void WithTheTeamReady_BothWaysAreOffered_AsOneChoice_WithSupportChosen()
    {
        var view = RenderView(SupportAssistantReadiness.Ready);

        var choice = Assert.Single(Choices(view));
        Assert.Contains("Assistant", choice.Markup);
        Assert.Contains("Support", choice.Markup);
        Assert.Equal(SupportAssistance.None, choice.Instance.Value);
    }

    [Fact]
    public async Task ChoosingTheAssistant_RaisesTheCaseWithIt_AndAsksItToAnswer()
    {
        var view = RenderView(SupportAssistantReadiness.Ready);

        var choice = Assert.Single(Choices(view));
        await view.InvokeAsync(() => choice.Instance.ValueChanged.InvokeAsync(SupportAssistance.Assistant));
        Send(view);

        _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Help, SupportAssistance.Assistant, default), Times.Once);
        _cases.Verify(x => x.RunAssistantAsync(TeamKey, "case-1", It.IsAny<IProgress<string>>(), default), Times.Once);
    }

    [Fact]
    public void LeavingTheChoiceAlone_RaisesTheCaseForAPerson()
    {
        var view = RenderView(SupportAssistantReadiness.Ready);

        Send(view);

        _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Help, SupportAssistance.None, default), Times.Once);
    }

    [Fact]
    public void OfferingOnlySupport_ShowsNoChoice_AndRaisesTheCaseForAPerson()
    {
        var view = RenderView(SupportAssistantReadiness.Ready, offered: [SupportAssistance.None]);

        Assert.Empty(Choices(view));
        Send(view);

        _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Help, SupportAssistance.None, default), Times.Once);
    }

    [Fact]
    public void OfferingOnlyTheAssistant_ShowsNoChoice_AndRaisesTheCaseWithIt()
    {
        var view = RenderView(SupportAssistantReadiness.Ready, offered: [SupportAssistance.Assistant]);

        Assert.Empty(Choices(view));
        Send(view);

        _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Help, SupportAssistance.Assistant, default), Times.Once);
    }

    [Fact]
    public void WithNoAssistantInTheDeployment_OnlySupportIsOffered_WhateverTheHostAsksFor()
    {
        var view = RenderView(SupportAssistantReadiness.NotRegistered, offered: [SupportAssistance.Assistant, SupportAssistance.None]);

        Assert.Empty(Choices(view));
        Send(view);

        _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Help, SupportAssistance.None, default), Times.Once);
    }

    [Fact]
    public void AskingOnlyForAnAssistantThatIsNotThere_FallsBackToSupport()
    {
        var view = RenderView(SupportAssistantReadiness.NotRegistered, offered: [SupportAssistance.Assistant]);

        Assert.Empty(Choices(view));
        Send(view);

        _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Help, SupportAssistance.None, default), Times.Once);
    }

    /// <summary>A deployment with an assistant, but no model for this team: the team is offered a person only.</summary>
    [Fact]
    public void ATeamWithoutAModelOfItsOwn_IsOfferedOnlySupport()
    {
        var view = RenderView(SupportAssistantReadiness.NotConfigured);

        Assert.Empty(Choices(view));
        Send(view);

        _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Help, SupportAssistance.None, default), Times.Once);
    }

    /// <summary>Silence would read as nobody caring, so a case the assistant did not answer says so, beside the way to a person.</summary>
    [Fact]
    public void WhenTheAssistantDoesNotAnswer_TheConversationSaysSo()
    {
        var view = RenderView(SupportAssistantReadiness.Ready, offered: [SupportAssistance.Assistant]);
        var withAssistant = Raised with { AssistantState = SupportAssistantState.Active };
        _cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 100, default)).ReturnsAsync(new SupportCasePage { Items = [withAssistant] });
        _cases.Setup(x => x.GetMessagesAsync(TeamKey, withAssistant.Id, null, SupportCaseLimits.MaxMessagesPerCase, default)).ReturnsAsync(new SupportMessagePage { Items = [] });
        _cases.Setup(x => x.GetVisibilityChoiceAsync(TeamKey, withAssistant.Id, default)).ReturnsAsync(new SupportCaseVisibilityChoice(false, false));
        _cases.Setup(x => x.RaiseCaseAsync(TeamKey, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SupportAssistance>(), default)).ReturnsAsync(withAssistant);
        _cases.Setup(x => x.RunAssistantAsync(TeamKey, withAssistant.Id, It.IsAny<IProgress<string>>(), default)).ReturnsAsync(false);

        Send(view);

        view.WaitForAssertion(() => Assert.Contains(SupportCasesViewText.AssistantDeclined.Default, view.Find($"[data-declined='{withAssistant.Id}']").TextContent));
    }

    [Fact]
    public void WhenTheAssistantAnswers_NothingIsSaidAboutDeclining()
    {
        var view = RenderView(SupportAssistantReadiness.Ready, offered: [SupportAssistance.Assistant]);
        var withAssistant = Raised with { AssistantState = SupportAssistantState.Active };
        _cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 100, default)).ReturnsAsync(new SupportCasePage { Items = [withAssistant] });
        _cases.Setup(x => x.GetMessagesAsync(TeamKey, withAssistant.Id, null, SupportCaseLimits.MaxMessagesPerCase, default)).ReturnsAsync(new SupportMessagePage { Items = [] });
        _cases.Setup(x => x.GetVisibilityChoiceAsync(TeamKey, withAssistant.Id, default)).ReturnsAsync(new SupportCaseVisibilityChoice(false, false));
        _cases.Setup(x => x.RaiseCaseAsync(TeamKey, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SupportAssistance>(), default)).ReturnsAsync(withAssistant);
        _cases.Setup(x => x.RunAssistantAsync(TeamKey, withAssistant.Id, It.IsAny<IProgress<string>>(), default)).ReturnsAsync(true);

        Send(view);

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll($"[data-conversation='{withAssistant.Id}']")));
        Assert.Empty(view.FindAll("[data-declined]"));
    }

    [Fact]
    public void TheAssistantsAnswer_ShowsInTheConversation_AsItIsWritten()
    {
        var view = RenderView(SupportAssistantReadiness.Ready, offered: [SupportAssistance.Assistant]);
        var withAssistant = Raised with { AssistantState = SupportAssistantState.Active };
        var kept = new TaskCompletionSource<bool>();
        _cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 100, default)).ReturnsAsync(new SupportCasePage { Items = [withAssistant] });
        _cases.Setup(x => x.GetMessagesAsync(TeamKey, withAssistant.Id, null, SupportCaseLimits.MaxMessagesPerCase, default)).ReturnsAsync(new SupportMessagePage { Items = [] });
        _cases.Setup(x => x.GetVisibilityChoiceAsync(TeamKey, withAssistant.Id, default)).ReturnsAsync(new SupportCaseVisibilityChoice(false, false));
        _cases.Setup(x => x.RaiseCaseAsync(TeamKey, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SupportAssistance>(), default)).ReturnsAsync(withAssistant);
        _cases.Setup(x => x.RunAssistantAsync(TeamKey, withAssistant.Id, It.IsAny<IProgress<string>>(), default))
            .Returns((string _, string _, IProgress<string> soFar, CancellationToken _) =>
            {
                soFar.Report("Check the date");
                return kept.Task;
            });

        Send(view);

        view.WaitForAssertion(() => Assert.Contains("Check the date", view.Find($"[data-conversation='{withAssistant.Id}'] [data-answer-so-far]").TextContent));
        Assert.DoesNotContain(SupportCasesViewText.AssistantThinking.Default, view.Markup);

        view.InvokeAsync(() => kept.SetResult(true));

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-answer-so-far]")));
    }

    [Fact]
    public void TheOptionsTexts_AreTranslatedThroughTheTextProvider()
    {
        var view = RenderView(SupportAssistantReadiness.Ready, texts: new Translating(new Dictionary<string, string>
        {
            [SupportCasesViewText.AnsweredByAssistant.Key] = "AI-assistent",
            [SupportCasesViewText.AnsweredByHuman.Key] = "Mänsklig support"
        }));

        var choice = Assert.Single(Choices(view));
        Assert.Contains("AI-assistent", choice.Markup);
        Assert.Contains("Mänsklig support", choice.Markup);
    }

    private sealed class Translating(Dictionary<string, string> texts) : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => Task.FromResult(texts.GetValueOrDefault(key.Key) ?? key.Default);
    }
}
