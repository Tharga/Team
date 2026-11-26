using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Help beside every page: who may be asked, the rail and the open panel, the conversation going through the same
/// case the support page shows, and what the panel remembers.
/// </summary>
public class SupportPanelTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string Question = "The export is empty.";

    private readonly Mock<ISupportCaseService> _cases = new();
    private readonly Mock<ISupportCaseNotifier> _notifier = new();
    private readonly Mock<ITeamStateService> _teamState = new();
    private readonly Mock<ILocalStorageService> _storage = new();
    private readonly TestTypingNotifier _typing = new();

    private static SupportCase Case(string id, SupportAssistantState state = SupportAssistantState.Active, SupportCaseStatus status = SupportCaseStatus.Open) => new()
    {
        Id = id,
        TeamKey = TeamKey,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = $"Subject of {id}",
        Status = status,
        CreatedAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        AssistantState = state
    };

    private static SupportMessage Message(int sequence, string body, SupportMessageKind kind, string author = "alice") => new()
    {
        Sequence = sequence,
        Kind = kind,
        AuthorIdentity = author,
        AuthorName = author,
        Body = body,
        SentAt = new DateTime(2026, 10, 24, 12, 0, sequence, DateTimeKind.Utc)
    };

    private IRenderedComponent<SupportPanel> Render(
        SupportAssistantReadiness readiness = SupportAssistantReadiness.Ready,
        SupportAssistance[] offered = null,
        ISupportPanelAvailability availability = null,
        bool open = false,
        int? width = null,
        bool withTeam = true,
        SupportPanelNarrowScreen narrowScreen = SupportPanelNarrowScreen.Hide,
        ISupportAssistantAllowanceService allowances = null,
        Microsoft.AspNetCore.Components.RenderFragment<SupportAssistantReadiness> top = null)
    {
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        _teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(withTeam ? team.Object : null);

        _cases.Setup(x => x.GetAssistantReadinessAsync(It.IsAny<string>(), default)).ReturnsAsync(readiness);
        _cases.Setup(x => x.GetMyUnreadCountAsync(TeamKey, default)).ReturnsAsync(2);

        _storage.Setup(x => x.GetItemAsync<bool?>("Tharga.Team.Support.Panel.Open", It.IsAny<CancellationToken>())).ReturnsAsync(open);
        _storage.Setup(x => x.GetItemAsync<int?>("Tharga.Team.Support.Panel.Width", It.IsAny<CancellationToken>())).ReturnsAsync(width);

        Services.AddSingleton(_cases.Object);
        Services.AddSingleton(_notifier.Object);
        Services.AddSingleton(_teamState.Object);
        Services.AddSingleton(_storage.Object);
        Services.AddSingleton<ISupportTypingNotifier>(_typing);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        if (availability != null) Services.AddSingleton(availability);
        if (allowances != null) Services.AddSingleton(allowances);
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        return Render<SupportPanel>(p => p.Add(x => x.Offered, offered).Add(x => x.NarrowScreen, narrowScreen).Add(x => x.AssistantTop, top));
    }

    private void Raises(SupportCase raised, bool answered = true, params SupportMessage[] messages)
    {
        _cases.Setup(x => x.RaiseCaseAsync(TeamKey, null, It.IsAny<string>(), It.IsAny<SupportAssistance>(), default)).ReturnsAsync(raised);
        _cases.Setup(x => x.RunAssistantAsync(TeamKey, raised.Id, It.IsAny<IProgress<SupportAssistantProgress>>(), default)).ReturnsAsync(answered);
        _cases.Setup(x => x.GetCaseAsync(TeamKey, raised.Id, default)).ReturnsAsync(raised);
        _cases.Setup(x => x.GetMessagesAsync(TeamKey, raised.Id, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportMessagePage { Items = messages });
    }

    private static void Send(IRenderedComponent<SupportPanel> view, string text)
    {
        view.Find("textarea[data-draft]").Input(text);
        view.Find("[data-send]").Click();
    }

    // ---- the message box ----

    /// <summary>One row to start with; script grows it as the text does, so a longer message is written in several rows rather than scrolled sideways.</summary>
    [Fact]
    public void TheMessageBox_IsATextArea_StartingAtOneRow()
    {
        var box = Render(open: true).Find("[data-draft]");

        Assert.Equal("textarea", box.TagName.ToLowerInvariant());
        Assert.Equal("1", box.GetAttribute("rows"));
    }

    [Fact]
    public void Enter_SendsTheMessage()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);

        view.Find("textarea[data-draft]").Input(Question);
        view.Find("textarea[data-draft]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        view.WaitForAssertion(() => _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Question, It.IsAny<SupportAssistance>(), default), Times.Once));
    }

    /// <summary>A new line is Shift+Enter, so what is typed stays in the box until it is sent.</summary>
    [Fact]
    public void ShiftEnter_DoesNotSend()
    {
        var view = Render(open: true);

        view.Find("textarea[data-draft]").Input("First line");
        view.Find("textarea[data-draft]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        _cases.Verify(x => x.RaiseCaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SupportAssistance>(), default), Times.Never);
    }

    [Fact]
    public void AMessageOfSeveralLines_IsSentAsWritten_AndShownWithItsLineBreaks()
    {
        const string written = "The export is empty.\nDate range: last week.\n\nCan you check?";
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, written, SupportMessageKind.User));
        var view = Render(open: true);

        Send(view, written);

        view.WaitForAssertion(() => _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, written, It.IsAny<SupportAssistance>(), default), Times.Once));
        view.WaitForAssertion(() => Assert.Equal(written, view.Find(".support-panel__msg--me").TextContent.Trim()));
    }

    // ---- the allowance bar ----

    private static SupportAssistantAllowance Allowance(decimal share = 0.4m, bool exhausted = false, bool applies = true, bool amounts = false) => new()
    {
        TeamKey = TeamKey,
        Applies = applies,
        Display = SupportAllowanceDisplay.Units,
        Unit = "WU",
        Share = share,
        Exhausted = exhausted,
        WarnAt = 0.8m,
        PeriodEndsAt = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
        Limit = amounts ? 10_000m : null,
        Used = amounts ? share * 10_000m : null,
        Models = [new SupportAssistantModelAllowance { ModelId = "m1", DisplayName = "Sonnet", Current = true, QuestionsLeft = amounts ? 60 : 60, CostPerAnswer = amounts ? 100m : null }]
    };

    private static ISupportAssistantAllowanceService Allowing(params SupportAssistantAllowance[] inOrder)
    {
        var service = new Mock<ISupportAssistantAllowanceService>();
        var sequence = service.SetupSequence(x => x.GetAllowanceAsync(TeamKey, default));
        foreach (var allowance in inOrder) sequence = sequence.ReturnsAsync(allowance);
        service.Setup(x => x.GetAllowanceAsync(TeamKey, default)).ReturnsAsync(inOrder[^1]);
        return service.Object;
    }

    [Fact]
    public void TheBar_IsShownByDefault_WhereTheTeamIsLimited()
    {
        var view = Render(open: true, allowances: Allowing(Allowance(0.4m)));

        var bar = view.Find("[data-allowance-bar]");
        Assert.Equal("40", bar.GetAttribute("aria-valuenow"));
        Assert.Contains("width: 40%", view.Find(".support-panel__allowance-fill").GetAttribute("style"));
        Assert.Contains("40% used", view.Find("[data-allowance-text]").TextContent);
    }

    [Fact]
    public void TheBar_CanBeTurnedOff_AndNothingElseChanges()
    {
        Services.Configure<ThargaBlazorOptions>(o => o.Support.ShowAllowance = false);

        var view = Render(open: true, allowances: Allowing(Allowance(0.4m)));

        Assert.Empty(view.FindAll("[data-allowance]"));
        Assert.NotNull(view.Find("[data-draft]"));
    }

    [Fact]
    public void ATeamWithNoLimit_HasNoBar()
        => Assert.Empty(Render(open: true, allowances: Allowing(Allowance(applies: false))).FindAll("[data-allowance]"));

    [Fact]
    public void WithNoAllowanceService_ThereIsNoBar_AndThePanelWorks()
    {
        var view = Render(open: true);

        Assert.Empty(view.FindAll("[data-allowance]"));
        Assert.NotNull(view.Find("[data-draft]"));
    }

    [Fact]
    public void AnAllowanceThatCannotBeRead_IsNoBar_AndNoFailure()
    {
        var service = new Mock<ISupportAssistantAllowanceService>();
        service.Setup(x => x.GetAllowanceAsync(TeamKey, default)).ThrowsAsync(new InvalidOperationException("The usage database is down."));

        var view = Render(open: true, allowances: service.Object);

        Assert.Empty(view.FindAll("[data-allowance]"));
        Assert.NotNull(view.Find("[data-draft]"));
    }

    [Fact]
    public void TheBar_IsForTheAssistant_NotForSupport()
    {
        var view = Render(open: true, allowances: Allowing(Allowance()));

        view.Find("[data-tab='support']").Click();

        Assert.Empty(view.FindAll("[data-allowance]"));
    }

    [Theory]
    [InlineData(0.79, false, "support-panel__allowance")]
    [InlineData(0.8, false, "support-panel__allowance support-panel__allowance--warn")]
    [InlineData(1.0, true, "support-panel__allowance support-panel__allowance--full")]
    public void TheBar_WarnsFromTheWarningShare_AndIsFullWhenUsedUp(double share, bool exhausted, string expected)
    {
        var view = Render(open: true, allowances: Allowing(Allowance((decimal)share, exhausted)));

        Assert.Equal(expected, view.Find("[data-allowance]").GetAttribute("class"));
    }

    /// <summary>99.7 percent is not 100: the bar is full only when nothing is left.</summary>
    [Fact]
    public void TheBar_IsNeverFullWhileThereIsSomethingLeft()
    {
        var view = Render(open: true, allowances: Allowing(Allowance(0.997m)));

        Assert.Equal("99", view.Find("[data-allowance-bar]").GetAttribute("aria-valuenow"));
    }

    [Fact]
    public void AnAdministrator_SeesTheAmounts_AndHowManyQuestionsAreLeft()
    {
        var view = Render(open: true, allowances: Allowing(Allowance(0.4m, amounts: true)));

        Assert.Contains($"{4_000m:N0} of {10_000m:N0} WU used", view.Find("[data-allowance-text]").TextContent);
        Assert.Contains("About 60 questions left on Sonnet", view.Find("[data-allowance-left]").TextContent);
    }

    /// <summary>A member is given the share and no amount, and so is not told how many questions are left either.</summary>
    [Fact]
    public void AMember_SeesOnlyTheShare()
    {
        var view = Render(open: true, allowances: Allowing(Allowance(0.4m)));

        Assert.DoesNotContain("WU", view.Find("[data-allowance]").TextContent);
        Assert.Empty(view.FindAll("[data-allowance-left]"));
    }

    [Fact]
    public void WhenItStartsAgain_IsSaid()
        => Assert.Contains(new DateTime(2026, 12, 1).ToString("d"), Render(open: true, allowances: Allowing(Allowance())).Find("[data-allowance-text]").TextContent);

    [Fact]
    public void TheBar_MovesAfterAnAnswer()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User), Message(2, "Check the date range.", SupportMessageKind.Assistant, author: null));
        var view = Render(open: true, allowances: Allowing(Allowance(0.4m), Allowance(0.5m)));

        Send(view, Question);

        view.WaitForAssertion(() => Assert.Equal("50", view.Find("[data-allowance-bar]").GetAttribute("aria-valuenow")));
    }

    /// <summary>Another member's answer spends the same allowance, so every open panel moves its bar.</summary>
    [Fact]
    public void TheBar_MovesWhenAnotherMembersAnswerIsAnnounced()
    {
        var view = Render(open: true, allowances: Allowing(Allowance(0.4m), Allowance(0.7m)));

        _notifier.Raise(x => x.CaseUpdated += null, _notifier.Object, new SupportCaseUpdatedEventArgs { TeamKey = TeamKey, CaseId = "someone-elses", Change = SupportCaseChange.Replied, FromChannel = false, FromRemote = true });

        view.WaitForAssertion(() => Assert.Equal("70", view.Find("[data-allowance-bar]").GetAttribute("aria-valuenow")));
    }

    [Fact]
    public void AReadingThatIsOnlyAReadMark_DoesNotAskAgain()
    {
        var service = Allowing(Allowance(0.4m));
        var view = Render(open: true, allowances: service);

        _notifier.Raise(x => x.CaseUpdated += null, _notifier.Object, new SupportCaseUpdatedEventArgs { TeamKey = TeamKey, CaseId = "x", Change = SupportCaseChange.Read, FromChannel = false });

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-allowance]")));
        Mock.Get(service).Verify(x => x.GetAllowanceAsync(TeamKey, default), Times.Once);
    }

    [Fact]
    public void WhenTheAllowanceIsUsedUp_TheTabStays_SaysWhy_AndTakesNoMessage()
    {
        var view = Render(SupportAssistantReadiness.AllowanceUsed, open: true, allowances: Allowing(Allowance(1.0m, exhausted: true)));

        Assert.NotNull(view.Find("[data-tab='assistant']"));
        Assert.Contains("allowance", view.Find("[data-assistant-paused]").TextContent);
        Assert.Contains(new DateTime(2026, 12, 1).ToString("d"), view.Find("[data-assistant-paused]").TextContent);
        Assert.True(view.Find("[data-draft]").HasAttribute("disabled"));
        Assert.True(view.Find("[data-send]").HasAttribute("disabled"));
        Assert.Equal("support-panel__allowance support-panel__allowance--full", view.Find("[data-allowance]").GetAttribute("class"));
    }

    /// <summary>Support is still there: the person can write to it, and the notice goes with the assistant tab.</summary>
    [Fact]
    public void WhenTheAllowanceIsUsedUp_SupportCanStillBeWrittenTo()
    {
        var view = Render(SupportAssistantReadiness.AllowanceUsed, open: true, allowances: Allowing(Allowance(1.0m, exhausted: true)));

        view.Find("[data-tab='support']").Click();

        Assert.Empty(view.FindAll("[data-assistant-paused]"));
        Assert.False(view.Find("[data-draft]").HasAttribute("disabled"));
    }

    [Fact]
    public void WhenTheProviderRefusedTheAccount_TheTabStays_AndSaysSo()
    {
        var view = Render(SupportAssistantReadiness.ProviderRefused, open: true);

        Assert.NotNull(view.Find("[data-tab='assistant']"));
        Assert.Contains("refused", view.Find("[data-assistant-paused]").TextContent);
        Assert.True(view.Find("[data-draft]").HasAttribute("disabled"));
    }

    private Mock<ISupportAiConnectionService> Connections(bool administrator = true)
    {
        var connections = new Mock<ISupportAiConnectionService>();
        if (administrator)
        {
            connections.Setup(x => x.GetChoicesAsync(TeamKey, default)).ReturnsAsync(new SupportAiConnectionChoices
            {
                Kinds = [SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Off],
                PlatformModels = [new SupportAiPlatformModelChoice { Id = "big", DisplayName = "Big", IsDefault = true }, new SupportAiPlatformModelChoice { Id = "small", DisplayName = "Small" }]
            });
            connections.Setup(x => x.GetConnectionAsync(TeamKey, default)).ReturnsAsync(new SupportAiConnection());
        }
        else
        {
            connections.Setup(x => x.GetChoicesAsync(TeamKey, default)).ThrowsAsync(new UnauthorizedAccessException());
        }

        Services.AddSingleton(connections.Object);
        return connections;
    }

    [Fact]
    public void WhenTheAssistantIsPaused_AnAdministratorIsOfferedWhatToDoAboutIt()
    {
        Connections();

        var view = Render(SupportAssistantReadiness.ProviderRefused, open: true);

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-refusal-actions] [data-switch-model='small']")));
        Assert.NotNull(view.Find("[data-refusal-actions] [data-use-own]"));
    }

    [Fact]
    public void WhenTheAssistantIsPaused_AMemberHasTheNoticeAlone()
    {
        Connections(administrator: false);

        var view = Render(SupportAssistantReadiness.ProviderRefused, open: true);

        Assert.NotNull(view.Find("[data-assistant-paused]"));
        Assert.Empty(view.FindAll("[data-refusal-actions]"));
    }

    /// <summary>Choosing another model asks again whether the assistant answers, and when it does the notice and the actions give way to the conversation.</summary>
    [Fact]
    public void ChoosingAnotherModel_ReadsTheReadinessAgain_AndTheNoticeGoesAway()
    {
        var connections = Connections();
        var view = Render(SupportAssistantReadiness.ProviderRefused, open: true);
        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-switch-model='small']")));
        _cases.Setup(x => x.GetAssistantReadinessAsync(It.IsAny<string>(), default)).ReturnsAsync(SupportAssistantReadiness.Ready);

        view.Find("[data-switch-model='small']").Click();

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-assistant-paused]")));
        Assert.Empty(view.FindAll("[data-refusal-actions]"));
        Assert.False(view.Find("[data-draft]").HasAttribute("disabled"));
        connections.Verify(x => x.UsePlatformModelAsync(TeamKey, "small", default), Times.Once);
    }

    // ---- whom to turn to ----

    [Theory]
    [InlineData(SupportAssistantReadiness.AllowanceUsed)]
    [InlineData(SupportAssistantReadiness.ProviderRefused)]
    [InlineData(SupportAssistantReadiness.Held)]
    public void WhereTheTeamHasSupport_TheNoticeSendsThemThere_AndOnlyInTheLineUnderIt(SupportAssistantReadiness readiness)
    {
        Connections(administrator: false);

        var view = Render(readiness, open: true);

        Assert.Equal(SupportPanelText.PausedWriteToSupport.Default, view.Find("[data-paused-support]").TextContent.Trim());
        Assert.DoesNotContain("support", view.Find("[data-assistant-paused]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(view.FindAll("[data-ask-administrator]"));
    }

    [Theory]
    [InlineData(SupportAssistantReadiness.AllowanceUsed)]
    [InlineData(SupportAssistantReadiness.ProviderRefused)]
    public void WhereTheTeamHasNoSupport_AMemberIsToldToAskAnAdministrator_NotToWriteToSupport(SupportAssistantReadiness readiness)
    {
        Connections(administrator: false);

        var view = Render(readiness, open: true, offered: [SupportAssistance.Assistant]);

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-ask-administrator]")));
        Assert.Empty(view.FindAll("[data-paused-support]"));
        Assert.DoesNotContain("support", view.Find("[data-log]").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhereTheTeamHasNoSupport_AnAdministratorGetsTheActions_NotAnInstructionToAskThemselves()
    {
        Connections();

        var view = Render(SupportAssistantReadiness.ProviderRefused, open: true, offered: [SupportAssistance.Assistant]);

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-use-own]")));
        Assert.Empty(view.FindAll("[data-ask-administrator]"));
        Assert.Empty(view.FindAll("[data-paused-support]"));
    }

    // ---- the host's own say ----

    /// <summary>The host's words are shown in place of ours, and the toolkit's own actions are not: whatever the team connects, the host's rule holds.</summary>
    [Fact]
    public void WhenTheHostHoldsTheAssistant_ItsMessageIsShown_AndTheToolkitsActionsAreNot()
    {
        Connections();
        var gate = new Mock<ISupportAssistantGate>();
        gate.Setup(x => x.CheckAsync(TeamKey, default)).ReturnsAsync(new SupportAssistantHold { Message = "Your plan does not include the assistant." });
        Services.AddSingleton(gate.Object);

        var view = Render(SupportAssistantReadiness.Held, open: true);

        Assert.Equal("Your plan does not include the assistant.", view.Find("[data-assistant-paused]").TextContent.Trim());
        Assert.Empty(view.FindAll("[data-refusal-actions]"));
        Assert.True(view.Find("[data-draft]").HasAttribute("disabled"));
    }

    [Fact]
    public void WhenTheHostHoldsTheAssistantWithoutWords_TheTabSaysItIsNotAvailable()
    {
        var view = Render(SupportAssistantReadiness.Held, open: true);

        Assert.Contains(SupportPanelText.AssistantHeld.Default, view.Find("[data-assistant-paused]").TextContent);
    }

    /// <summary>A gate that fails leaves the generic notice rather than a blank panel.</summary>
    [Fact]
    public void AGateThatFailsToAnswerTheMessage_LeavesTheGenericNotice()
    {
        var gate = new Mock<ISupportAssistantGate>();
        gate.Setup(x => x.CheckAsync(TeamKey, default)).ThrowsAsync(new InvalidOperationException());
        Services.AddSingleton(gate.Object);

        var view = Render(SupportAssistantReadiness.Held, open: true);

        Assert.Contains(SupportPanelText.AssistantHeld.Default, view.Find("[data-assistant-paused]").TextContent);
    }

    [Theory]
    [InlineData(SupportAssistantReadiness.Ready)]
    [InlineData(SupportAssistantReadiness.AllowanceUsed)]
    [InlineData(SupportAssistantReadiness.ProviderRefused)]
    [InlineData(SupportAssistantReadiness.Held)]
    public void TheHostsContent_IsShownAtTheTopOfTheAssistantTab_ToldWhyTheAssistantIsOrIsNotAnswering(SupportAssistantReadiness readiness)
    {
        var view = Render(readiness, open: true, top: state => builder => builder.AddContent(0, $"state:{state}"));

        Assert.Equal($"state:{readiness}", view.Find("[data-assistant-top]").TextContent);
    }

    [Fact]
    public void TheHostsContent_IsNotShownTowardsSupport()
    {
        var view = Render(open: true, top: state => builder => builder.AddContent(0, "x"));

        view.Find("[data-tab='support']").Click();

        Assert.Empty(view.FindAll("[data-assistant-top]"));
    }

    [Fact]
    public void WithoutHostContent_NothingIsAdded()
        => Assert.Empty(Render(open: true).FindAll("[data-assistant-top]"));

    [Fact]
    public void WithAnAssistantReady_NothingIsPaused()
    {
        var view = Render(open: true, allowances: Allowing(Allowance()));

        Assert.Empty(view.FindAll("[data-assistant-paused]"));
        Assert.False(view.Find("[data-draft]").HasAttribute("disabled"));
    }

    /// <summary>The date a used-up allowance starts again is read even where the host has turned the bar off.</summary>
    [Fact]
    public void WithTheBarOff_AUsedUpAllowance_StillSaysWhenItStartsAgain()
    {
        Services.Configure<ThargaBlazorOptions>(o => o.Support.ShowAllowance = false);

        var view = Render(SupportAssistantReadiness.AllowanceUsed, open: true, allowances: Allowing(Allowance(1.0m, exhausted: true)));

        Assert.Empty(view.FindAll("[data-allowance]"));
        Assert.Contains(new DateTime(2026, 12, 1).ToString("d"), view.Find("[data-assistant-paused]").TextContent);
    }

    [Fact]
    public void TheRail_HasAButtonPerChannel_AndTheUnreadCount()
    {
        var view = Render();

        Assert.Equal("rail", view.Find("[data-support-panel]").GetAttribute("data-support-panel"));
        Assert.NotNull(view.Find("[data-open='assistant']"));
        Assert.NotNull(view.Find("[data-open='support']"));
        Assert.Equal("2", view.Find("[data-unread]").TextContent.Trim());
    }

    /// <summary>A team on Off, or without a model, is never offered the assistant.</summary>
    [Fact]
    public void ATeamNotReadyForTheAssistant_IsOfferedSupportOnly()
    {
        var view = Render(SupportAssistantReadiness.NotConfigured);

        Assert.Empty(view.FindAll("[data-open='assistant']"));
        Assert.NotNull(view.Find("[data-open='support']"));
    }

    [Fact]
    public void Offered_NarrowsWhoMayBeAsked()
    {
        var view = Render(offered: [SupportAssistance.None]);

        Assert.Empty(view.FindAll("[data-open='assistant']"));
        Assert.NotNull(view.Find("[data-open='support']"));
    }

    [Fact]
    public void TheHostsAvailability_Narrows_AndItsChangeRedraws()
    {
        var availability = new SwitchableAvailability([SupportAssistance.Assistant]);
        var view = Render(availability: availability);

        Assert.Empty(view.FindAll("[data-open='support']"));
        Assert.NotNull(view.Find("[data-open='assistant']"));

        availability.Switch([SupportAssistance.None]);

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-open='support']")));
        Assert.Empty(view.FindAll("[data-open='assistant']"));
    }

    [Fact]
    public void WithNobodyToAsk_ThePanelIsNotDrawn()
    {
        var view = Render(availability: new SwitchableAvailability([]));

        Assert.Empty(view.FindAll("[data-support-panel]"));
    }

    [Fact]
    public void WithNoTeam_ThePanelIsNotDrawn()
    {
        var view = Render(withTeam: false);

        Assert.Empty(view.FindAll("[data-support-panel]"));
    }

    [Fact]
    public void Opening_IsRemembered()
    {
        var view = Render();

        view.Find("[data-open='assistant']").Click();

        Assert.Equal("open", view.Find("[data-support-panel]").GetAttribute("data-support-panel"));
        _storage.Verify(x => x.SetItemAsync("Tharga.Team.Support.Panel.Open", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ARememberedOpenStateAndWidth_AreWhereItStarts()
    {
        var view = Render(open: true, width: 420);

        Assert.Equal("open", view.Find("[data-support-panel]").GetAttribute("data-support-panel"));
        Assert.Contains("width: 420px", view.Find(".support-panel-sidebar").GetAttribute("style"));
    }

    /// <summary>A drag ends with the width the panel was dragged to; the sidebar is drawn at it, and it is remembered.</summary>
    [Fact]
    public async Task ADraggedWidth_IsWhatThePanelIsDrawnAt_AndIsRemembered()
    {
        var view = Render(open: true);

        await view.InvokeAsync(() => view.Instance.OnResized(512));

        Assert.Contains("width: 512px", view.Find(".support-panel-sidebar").GetAttribute("style"));
        _storage.Verify(x => x.SetItemAsync("Tharga.Team.Support.Panel.Width", 512, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A host that sets nothing gets the panel hidden on a narrow screen, as before (Tharga/Team#419).</summary>
    [Fact]
    public void ByDefault_ThereIsNoDrawer()
    {
        var view = Render();

        Assert.DoesNotContain("support-panel-sidebar--drawer", view.Find(".support-panel-sidebar").GetAttribute("class"));
        Assert.Empty(view.FindAll("[data-open='panel']"));
    }

    [Fact]
    public void AsADrawer_TheSidebarIsMarked_CollapsedAndOpen()
    {
        var view = Render(narrowScreen: SupportPanelNarrowScreen.Drawer);
        Assert.Contains("support-panel-sidebar--drawer", view.Find(".support-panel-sidebar").GetAttribute("class"));

        view.Find("[data-open='panel']").Click();

        Assert.Equal("open", view.Find("[data-support-panel]").GetAttribute("data-support-panel"));
        Assert.Contains("support-panel-sidebar--drawer", view.Find(".support-panel-sidebar").GetAttribute("class"));
    }

    /// <summary>Collapsed on a narrow screen there is one button instead of the rail, so it carries what the rail would.</summary>
    [Fact]
    public void AsADrawer_TheFloatingButton_CarriesTheUnreadCount()
    {
        var view = Render(narrowScreen: SupportPanelNarrowScreen.Drawer);

        Assert.Equal("2", view.Find("[data-open='panel'] [data-unread]").TextContent.Trim());
    }

    [Fact]
    public void AsADrawer_TheFloatingButton_OpensTheAssistantWhenBothAreOffered()
    {
        var view = Render(narrowScreen: SupportPanelNarrowScreen.Drawer);

        view.Find("[data-open='panel']").Click();

        Assert.Equal("true", view.Find("[data-tab='assistant']").GetAttribute("aria-selected"));
    }

    [Fact]
    public void AsADrawer_TheFloatingButton_OpensSupportWhenOnlySupportIsOffered()
    {
        var view = Render(offered: [SupportAssistance.None], narrowScreen: SupportPanelNarrowScreen.Drawer);

        view.Find("[data-open='panel']").Click();

        Assert.Equal("true", view.Find("[data-tab='support']").GetAttribute("aria-selected"));
    }

    [Fact]
    public void AWidthBelowTheMinimum_IsRaisedToIt()
    {
        var view = Render(open: true, width: 100);

        Assert.Contains("width: 280px", view.Find(".support-panel-sidebar").GetAttribute("style"));
    }

    [Fact]
    public void AMessageToTheAssistant_RaisesACaseWithIt_AndAsksItToAnswer()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User), Message(2, "Check the date range.", SupportMessageKind.Assistant, author: null));
        var view = Render(open: true);

        Send(view, Question);

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-message]").Count));
        _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Question, SupportAssistance.Assistant, default), Times.Once);
        _cases.Verify(x => x.RunAssistantAsync(TeamKey, "case-1", It.IsAny<IProgress<SupportAssistantProgress>>(), default), Times.Once);
        Assert.Contains("Check the date range.", view.FindAll("[data-message]")[1].TextContent);
    }

    /// <summary>Silence would read as nobody caring, so a question the assistant did not answer says so.</summary>
    [Fact]
    public void WhenTheAssistantDoesNotAnswer_ItSaysSo()
    {
        Raises(Case("case-1"), answered: false, Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);

        Send(view, Question);

        view.WaitForAssertion(() => Assert.Contains(SupportCasesViewText.AssistantDeclined.Default, view.Find("[data-declined]").TextContent));
    }

    [Fact]
    public void WritingToSupport_OnAConversationWithTheAssistant_HandsItOver()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        Send(view, Question);
        view.WaitForAssertion(() => _cases.Verify(x => x.RunAssistantAsync(TeamKey, "case-1", It.IsAny<IProgress<SupportAssistantProgress>>(), default), Times.Once));

        view.Find("[data-tab='support']").Click();
        Send(view, "Please ask a person.");

        view.WaitForAssertion(() => _cases.Verify(x => x.ReplyToCaseAsync(TeamKey, "case-1", "Please ask a person.", default), Times.Once));
        _cases.Verify(x => x.RequestHumanAsync(TeamKey, "case-1", default), Times.Once);
        _cases.Verify(x => x.RunAssistantAsync(TeamKey, "case-1", It.IsAny<IProgress<SupportAssistantProgress>>(), default), Times.Once);
    }

    /// <summary>The tab says who the message goes to, and the assistant is never handed a file.</summary>
    [Fact]
    public void FilesForSupportOnly_AreOfferedOnTheSupportTab_AndNeverToTheAssistant()
    {
        AcceptsSecureFiles();
        var view = Render(open: true);

        Assert.Empty(view.FindAll("[data-secure-files]"));

        view.Find("[data-tab='support']").Click();

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-secure-files] [data-attachment-picker='secure']")));
    }

    [Fact]
    public void AMessageToSupport_WithAFileForSupportOnly_RaisesTheCaseWithIt()
    {
        AcceptsSecureFiles();
        var raised = Case("case-1", SupportAssistantState.None);
        Raises(raised, messages: Message(1, Question, SupportMessageKind.User));
        _cases.Setup(x => x.RaiseCaseAsync(TeamKey, null, Question, It.IsAny<SupportRaiseOptions>(), default)).ReturnsAsync(raised);
        var view = Render(open: true);

        view.Find("[data-tab='support']").Click();
        view.WaitForAssertion(() => view.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>());
        view.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2], "salaries.xlsx", contentType: "application/octet-stream"));
        Send(view, Question);

        view.WaitForAssertion(() => _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Question,
            It.Is<SupportRaiseOptions>(o => o.Assistance == SupportAssistance.None && o.SecureAttachments.Length == 1 && o.SecureAttachments[0].FileName == "salaries.xlsx"), default), Times.Once));
        _cases.Verify(x => x.RaiseCaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SupportAssistance>(), default), Times.Never);
    }

    [Fact]
    public void AReplyToSupport_WithAFileForSupportOnly_CarriesIt_AndTheFileIsShownWithTheMessage()
    {
        AcceptsSecureFiles();
        var raised = Case("case-1", SupportAssistantState.None);
        var stored = new SupportAttachment { Id = "s1", FileName = "salaries.xlsx", ContentType = "application/octet-stream", Size = 2, Secure = true, UploadedBy = "alice" };
        Raises(raised, messages: [Message(1, Question, SupportMessageKind.User), Message(2, "Here it is.", SupportMessageKind.User) with { Attachments = [stored] }]);
        var view = Render(open: true);
        view.Find("[data-tab='support']").Click();
        Send(view, Question);
        view.WaitForAssertion(() => _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Question, SupportAssistance.None, default), Times.Once));

        view.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1, 2], "salaries.xlsx", contentType: "application/octet-stream"));
        Send(view, "Here it is.");

        view.WaitForAssertion(() => _cases.Verify(x => x.ReplyToCaseAsync(TeamKey, "case-1", "Here it is.", null,
            It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 0),
            It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 1 && f[0].FileName == "salaries.xlsx"), default), Times.Once));
        view.WaitForAssertion(() => Assert.Contains("salaries.xlsx", view.Find("[data-panel-file='s1']").TextContent));
    }

    private void AcceptsSecureFiles()
        => _cases.Setup(x => x.GetAttachmentLimitsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SupportAttachmentLimits.Default with { AcceptsSecureFiles = true, SecureKeepFor = TimeSpan.FromDays(30) });

    [Fact]
    public void AMessageToSupport_RaisesACaseForAPerson()
    {
        Raises(Case("case-1", SupportAssistantState.None), messages: Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);

        view.Find("[data-tab='support']").Click();
        Send(view, Question);

        view.WaitForAssertion(() => _cases.Verify(x => x.RaiseCaseAsync(TeamKey, null, Question, SupportAssistance.None, default), Times.Once));
        _cases.Verify(x => x.RunAssistantAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<SupportAssistantProgress>>(), default), Times.Never);
    }

    [Fact]
    public void EarlierConversations_ListTheOpenOnes_AndOpenOne()
    {
        var open = Case("case-open", SupportAssistantState.None);
        var closed = Case("case-closed", SupportAssistantState.None, SupportCaseStatus.Closed);
        _cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportCasePage { Items = [open, closed] });
        _cases.Setup(x => x.GetCaseAsync(TeamKey, open.Id, default)).ReturnsAsync(open);
        _cases.Setup(x => x.GetMessagesAsync(TeamKey, open.Id, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportMessagePage { Items = [Message(1, Question, SupportMessageKind.User)] });
        var view = Render(open: true);

        view.Find("[data-earlier]").Click();

        var sessions = view.FindAll("[data-session]").Select(x => x.GetAttribute("data-session"));
        Assert.Equal(["case-open"], sessions);

        view.Find("[data-session='case-open']").Click();

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-message]")));
        Assert.Equal("true", view.Find("[data-tab='support']").GetAttribute("aria-selected"));
    }

    /// <summary>A reply from Slack or mail is raised on whichever instance took it in; the open conversation reads it again.</summary>
    [Fact]
    public void AnUpdateToTheOpenConversation_RedrawsIt()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        Send(view, Question);
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-message]")));

        _cases.Setup(x => x.GetMessagesAsync(TeamKey, raised.Id, null, It.IsAny<int>(), default))
            .ReturnsAsync(new SupportMessagePage { Items = [Message(1, Question, SupportMessageKind.User), Message(2, "From support.", SupportMessageKind.User, author: "staff")] });
        _notifier.Raise(x => x.CaseUpdated += null, _notifier.Object, new SupportCaseUpdatedEventArgs { TeamKey = TeamKey, CaseId = raised.Id, Change = SupportCaseChange.Replied, FromChannel = true });

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-message]").Count));
    }

    /// <summary>An answer written on another instance reaches this one only when the host relays it (Tharga/Team#420); then the open conversation and the unread count are read again.</summary>
    [Fact]
    public void AChangeRelayedFromAnotherInstance_RedrawsTheOpenConversation_AndTheUnreadCount()
    {
        var raised = Case("case-1", SupportAssistantState.None);
        Raises(raised, messages: Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        view.Find("[data-tab='support']").Click();
        Send(view, Question);
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-message]")));

        _cases.Setup(x => x.GetMessagesAsync(TeamKey, raised.Id, null, It.IsAny<int>(), default))
            .ReturnsAsync(new SupportMessagePage { Items = [Message(1, Question, SupportMessageKind.User), Message(2, "From support.", SupportMessageKind.User, author: "staff")] });
        _cases.Setup(x => x.GetMyUnreadCountAsync(TeamKey, default)).ReturnsAsync(3);
        _notifier.Raise(x => x.CaseUpdated += null, _notifier.Object,
            new SupportCaseUpdatedEventArgs { TeamKey = TeamKey, CaseId = raised.Id, Change = SupportCaseChange.Replied, FromChannel = false, FromRemote = true });

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-message]").Count));
        Assert.Equal("3", view.Find("[data-unread]").TextContent.Trim());
    }

    [Fact]
    public void TheAssistantsAnswer_ShowsAsItIsWritten_ThenAsKept()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User));
        var kept = new TaskCompletionSource<bool>();
        _cases.Setup(x => x.RunAssistantAsync(TeamKey, raised.Id, It.IsAny<IProgress<SupportAssistantProgress>>(), default))
            .Returns((string _, string _, IProgress<SupportAssistantProgress> soFar, CancellationToken _) =>
            {
                soFar.Report(new SupportAssistantProgress { AnswerSoFar = "Check the" });
                soFar.Report(new SupportAssistantProgress { AnswerSoFar = "Check the date range" });
                return kept.Task;
            });
        var view = Render(open: true);

        Send(view, Question);

        view.WaitForAssertion(() => Assert.Contains("Check the date range", view.Find("[data-answer-so-far]").TextContent));
        Assert.Empty(view.FindAll("[data-working]"));

        _cases.Setup(x => x.GetMessagesAsync(TeamKey, raised.Id, null, It.IsAny<int>(), default))
            .ReturnsAsync(new SupportMessagePage { Items = [Message(1, Question, SupportMessageKind.User), Message(2, "Check the date range.", SupportMessageKind.Assistant, author: null)] });
        view.InvokeAsync(() => kept.SetResult(true));

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-message]").Count));
        Assert.Empty(view.FindAll("[data-answer-so-far]"));
    }

    [Fact]
    public void WhileTheAssistantWorks_ThePanelSaysHowManyTimesItHasLooped()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User));
        var kept = new TaskCompletionSource<bool>();
        IProgress<SupportAssistantProgress> report = null;
        _cases.Setup(x => x.RunAssistantAsync(TeamKey, raised.Id, It.IsAny<IProgress<SupportAssistantProgress>>(), default))
            .Returns((string _, string _, IProgress<SupportAssistantProgress> progress, CancellationToken _) =>
            {
                report = progress;
                progress.Report(new SupportAssistantProgress { Round = 1, Tools = ["get_my_cases"], Calls = [new SupportAssistantCall { Name = "get_my_cases" }] });
                return kept.Task;
            });
        var view = Render(open: true);

        Send(view, Question);

        view.WaitForAssertion(() => Assert.Equal("Working 1", view.Find("[data-working] .support-typing__label--shown").TextContent));

        SupportAssistantCall[] made = [new() { Name = "get_my_cases" }, new() { Name = "search_my_cases", Arguments = "text: \"export\"" }];
        view.InvokeAsync(() => report.Report(new SupportAssistantProgress { Round = 2, AnswerSoFar = "You have two", Tools = ["search_my_cases"], Calls = made }));

        view.WaitForAssertion(() => Assert.Equal("Working 2", view.Find("[data-answer-so-far] [data-step] .support-typing__label--shown").TextContent));

        view.InvokeAsync(() => report.Report(new SupportAssistantProgress { Round = 3, AnswerSoFar = "You have two cases.", Calls = made }));

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-step]")));
        view.InvokeAsync(() => kept.SetResult(true));
    }

    [Fact]
    public void TheHostsDisplaySettings_ApplyToThePanel()
    {
        Services.Configure<ThargaBlazorOptions>(o =>
        {
            o.Support.RateAnswers = false;
            o.Support.AnswerDetailsShownTo = SupportAnswerDetailsAudience.Support;
        });
        var answer = Message(2, "You have two cases.", SupportMessageKind.Assistant, author: null) with { AssistantCalls = [new() { Name = "get_my_cases" }] };
        Raises(Case("case-1"), answered: true, Message(1, Question, SupportMessageKind.User), answer);
        var view = Render(open: true);

        Send(view, Question);

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-message]").Count));
        Assert.Empty(view.FindAll("[data-rate]"));
        Assert.Empty(view.FindAll("[data-calls]"));
    }

    [Fact]
    public void AnAnswer_ShowsTheServiceCallsItMade_Folded()
    {
        var raised = Case("case-1");
        var answer = Message(2, "You have two cases.", SupportMessageKind.Assistant, author: null) with
        {
            AssistantCalls = [new() { Name = "get_my_cases" }, new() { Name = "search_my_cases", Arguments = "text: \"export\"" }]
        };
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User), answer);
        var view = Render(open: true);

        Send(view, Question);

        view.WaitForAssertion(() => Assert.Equal("2 service calls", view.Find("[data-calls='2'] summary").TextContent.Trim()));
        Assert.Equal(["get_my_cases()", "search_my_cases(text: \"export\")"], view.FindAll("[data-calls='2'] [data-call]").Select(x => x.TextContent.Trim()));
    }

    [Fact]
    public void AnAnswerByTheAssistant_CanBeRated_AndTheSameAgainTakesItBack()
    {
        Services.Configure<ThargaBlazorOptions>(o => o.Support.RateAnswers = true);
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User), Message(2, "Check the date range.", SupportMessageKind.Assistant, author: null));
        var view = Render(open: true);
        Send(view, Question);
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-rate]")));

        view.Find("[data-rate='2'] [data-rate-choice='Up']").Click();

        view.WaitForAssertion(() => Assert.Equal("true", view.Find("[data-rate='2'] [data-rate-choice='Up']").GetAttribute("aria-pressed")));
        _cases.Verify(x => x.RateAnswerAsync(TeamKey, raised.Id, 2, SupportAnswerRating.Up, default), Times.Once);

        view.Find("[data-rate='2'] [data-rate-choice='Up']").Click();

        view.WaitForAssertion(() => Assert.Equal("false", view.Find("[data-rate='2'] [data-rate-choice='Up']").GetAttribute("aria-pressed")));
        _cases.Verify(x => x.RateAnswerAsync(TeamKey, raised.Id, 2, SupportAnswerRating.None, default), Times.Once);
    }

    private IRenderedComponent<SupportPanel> AnsweredConversation(SupportCase raised, bool rateConversations = true)
    {
        Services.Configure<ThargaBlazorOptions>(o => o.Support.RateConversations = rateConversations);
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User), Message(2, "Check the date range.", SupportMessageKind.Assistant, author: null));
        var view = Render(open: true);
        Send(view, Question);
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-message]").Count));
        return view;
    }

    [Fact]
    public void ByDefault_TheConversationIsNotOfferedForRating()
    {
        var view = AnsweredConversation(Case("case-1"), rateConversations: false);

        Assert.Empty(view.FindAll("[data-rate-conversation]"));
    }

    [Fact]
    public void AConversationNobodyButAPersonAnswered_IsNotOfferedForRating()
    {
        Services.Configure<ThargaBlazorOptions>(o => o.Support.RateConversations = true);
        var raised = Case("case-1", SupportAssistantState.None);
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User), Message(2, "We are on it.", SupportMessageKind.User, author: "staff") with { AsSupport = true });
        var view = Render(open: true, offered: [SupportAssistance.None]);

        Send(view, Question);

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-message]").Count));
        Assert.Empty(view.FindAll("[data-rate-conversation]"));
    }

    [Fact]
    public void ThumbsUp_RatesTheConversation_AndThanks()
    {
        var raised = Case("case-1");
        var view = AnsweredConversation(raised);

        view.Find("[data-rate-conversation-choice='Up']").Click();

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-rate-conversation-thanks]")));
        Assert.Equal("true", view.Find("[data-rate-conversation-choice='Up']").GetAttribute("aria-pressed"));
        Assert.Empty(view.FindAll("[data-rate-conversation-comment]"));
        _cases.Verify(x => x.RateConversationAsync(TeamKey, raised.Id, SupportAnswerRating.Up, null, default), Times.Once);
    }

    [Fact]
    public void ThumbsDown_AsksWhy_SaysWhatTheFeedbackIsFor_AndSendsTheComment()
    {
        var raised = Case("case-1");
        var view = AnsweredConversation(raised);

        view.Find("[data-rate-conversation-choice='Down']").Click();

        Assert.Contains("improve how the assistant works", view.Find("[data-rate-conversation-notice]").TextContent);
        _cases.Verify(x => x.RateConversationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SupportAnswerRating>(), It.IsAny<string>(), default), Times.Never);

        view.Find("textarea[data-rate-conversation-comment]").Input("It never looked at my settings.");
        view.Find("[data-rate-conversation-send]").Click();

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-rate-conversation-thanks]")));
        Assert.Empty(view.FindAll("[data-rate-conversation-comment]"));
        Assert.Equal("true", view.Find("[data-rate-conversation-choice='Down']").GetAttribute("aria-pressed"));
        _cases.Verify(x => x.RateConversationAsync(TeamKey, raised.Id, SupportAnswerRating.Down, "It never looked at my settings.", default), Times.Once);
    }

    [Fact]
    public void ThumbsDown_CanBeSentWithoutAComment()
    {
        var raised = Case("case-1");
        var view = AnsweredConversation(raised);

        view.Find("[data-rate-conversation-choice='Down']").Click();
        view.Find("[data-rate-conversation-send]").Click();

        view.WaitForAssertion(() => _cases.Verify(x => x.RateConversationAsync(TeamKey, raised.Id, SupportAnswerRating.Down, null, default), Times.Once));
    }

    [Fact]
    public void ThumbsUpAgain_TakesTheRatingBack()
    {
        var raised = Case("case-1");
        var view = AnsweredConversation(raised);
        view.Find("[data-rate-conversation-choice='Up']").Click();
        view.WaitForAssertion(() => Assert.Equal("true", view.Find("[data-rate-conversation-choice='Up']").GetAttribute("aria-pressed")));

        view.Find("[data-rate-conversation-choice='Up']").Click();

        view.WaitForAssertion(() => Assert.Equal("false", view.Find("[data-rate-conversation-choice='Up']").GetAttribute("aria-pressed")));
        _cases.Verify(x => x.RateConversationAsync(TeamKey, raised.Id, SupportAnswerRating.None, null, default), Times.Once);
    }

    [Fact]
    public void AnEarlierRating_ShowsAsChosen()
    {
        var raised = Case("case-1") with { ConversationRating = new SupportConversationRating { Rating = SupportAnswerRating.Down, RatedAt = DateTime.UnixEpoch, RatedBy = "alice" } };

        var view = AnsweredConversation(raised);

        Assert.Equal("true", view.Find("[data-rate-conversation-choice='Down']").GetAttribute("aria-pressed"));
        Assert.Empty(view.FindAll("[data-rate-conversation-thanks]"));
    }

    [Fact]
    public void ARatingThatFails_KeepsTheComment_AndSaysSo()
    {
        var raised = Case("case-1");
        _cases.Setup(x => x.RateConversationAsync(TeamKey, raised.Id, SupportAnswerRating.Down, It.IsAny<string>(), default)).ThrowsAsync(new InvalidOperationException("down"));
        var view = AnsweredConversation(raised);
        view.Find("[data-rate-conversation-choice='Down']").Click();
        view.Find("textarea[data-rate-conversation-comment]").Input("Too slow.");

        view.Find("[data-rate-conversation-send]").Click();

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-rate-conversation-failed]")));
        Assert.Equal("Too slow.", view.Find("textarea[data-rate-conversation-comment]").GetAttribute("value"));
        Assert.Empty(view.FindAll("[data-rate-conversation-thanks]"));
    }

    [Fact]
    public void ANewConversation_StartsUnrated()
    {
        var view = AnsweredConversation(Case("case-1"));
        view.Find("[data-rate-conversation-choice='Up']").Click();
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-rate-conversation-thanks]")));

        view.Find("[data-new]").Click();

        Assert.Empty(view.FindAll("[data-rate-conversation]"));
    }

    [Fact]
    public void BeforeTheFirstWord_ThePanelSaysItIsWorking()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User));
        var kept = new TaskCompletionSource<bool>();
        _cases.Setup(x => x.RunAssistantAsync(TeamKey, raised.Id, It.IsAny<IProgress<SupportAssistantProgress>>(), default)).Returns(kept.Task);
        var view = Render(open: true);

        Send(view, Question);

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-working]")));
        Assert.Equal(3, view.FindAll("[data-working] .support-typing__dot").Count);
        Assert.Equal("Working", view.Find("[data-working] .support-typing__label--shown").TextContent);
        Assert.Empty(view.FindAll("[data-answer-so-far]"));
        Assert.Empty(view.FindAll("[data-step]"));
        view.InvokeAsync(() => kept.SetResult(true));
    }

    [Fact]
    public void SupportWriting_ShowsDotsInTheOpenConversation_UntilTheAnswerArrives()
    {
        var raised = Case("case-1", SupportAssistantState.None);
        Raises(raised, messages: Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        view.Find("[data-tab='support']").Click();
        Send(view, Question);
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-message]")));

        _typing.Writes(TeamKey, raised.Id, "staff", asSupport: true, name: "Sam");

        view.WaitForAssertion(() => Assert.Equal("Sam is typing…", view.Find("[data-writing] .support-typing__label").TextContent));

        _cases.Setup(x => x.GetMessagesAsync(TeamKey, raised.Id, null, It.IsAny<int>(), default))
            .ReturnsAsync(new SupportMessagePage { Items = [Message(1, Question, SupportMessageKind.User), Message(2, "From support.", SupportMessageKind.User, author: "staff")] });
        _notifier.Raise(x => x.CaseUpdated += null, _notifier.Object, new SupportCaseUpdatedEventArgs { TeamKey = TeamKey, CaseId = raised.Id, Change = SupportCaseChange.Replied, FromChannel = false });

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-message]").Count));
        Assert.Empty(view.FindAll("[data-writing]"));
    }

    [Fact]
    public void TheMembersOwnWriting_ShowsNoDots()
    {
        var raised = Case("case-1", SupportAssistantState.None);
        Raises(raised, messages: Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        view.Find("[data-tab='support']").Click();
        Send(view, Question);
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-message]")));

        _typing.Writes(TeamKey, raised.Id, "alice", asSupport: false);

        Assert.Empty(view.FindAll("[data-writing]"));
    }

    [Fact]
    public void WritingToSupport_InAnOpenConversation_TellsSupport()
    {
        var raised = Case("case-1", SupportAssistantState.None);
        Raises(raised, messages: Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        view.Find("[data-tab='support']").Click();
        Send(view, Question);
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-message]")));

        view.Find("textarea[data-draft]").Input("And the");
        view.Find("textarea[data-draft]").Input("And the date");

        view.WaitForAssertion(() => _cases.Verify(x => x.ReportTypingAsync(TeamKey, raised.Id, false, default), Times.Once));
    }

    /// <summary>The assistant answers what is sent; nobody is waiting on the other side to be told.</summary>
    [Fact]
    public void WritingToTheAssistant_TellsNobody()
    {
        var raised = Case("case-1");
        Raises(raised, answered: true, Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        Send(view, Question);
        view.WaitForAssertion(() => _cases.Verify(x => x.RunAssistantAsync(TeamKey, "case-1", It.IsAny<IProgress<SupportAssistantProgress>>(), default), Times.Once));

        view.Find("textarea[data-draft]").Input("And the date");

        _cases.Verify(x => x.ReportTypingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), default), Times.Never);
    }

    [Fact]
    public void AnotherTeam_StartsOver_AndAsksAgainWhoMayBeAsked()
    {
        var view = Render(open: true);
        var other = new Mock<ITeam>();
        other.SetupGet(x => x.Key).Returns("globex");

        _teamState.Raise(x => x.SelectedTeamChangedEvent += null, _teamState.Object, new SelectedTeamChangedEventArgs(other.Object));

        view.WaitForAssertion(() => _cases.Verify(x => x.GetAssistantReadinessAsync("globex", default), Times.Once));
    }

    [Fact]
    public void TheSupportTab_SaysWhetherSupportIsOnline_AndFollowsIt()
    {
        var presence = new TestPresence(SupportPresenceState.Online);
        Services.AddSingleton<ISupportPresence>(presence);
        var view = Render(open: true);

        Assert.Empty(view.FindAll("[data-presence]"));

        view.Find("[data-tab='support']").Click();
        Assert.Contains("Support is online", view.Find("[data-presence='Online']").TextContent);

        presence.Becomes(SupportPresenceState.Away);

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-presence='Away']")));
    }

    [Fact]
    public void WithNothingKnownAboutSupport_NoIndicatorShows()
    {
        Services.AddSingleton<ISupportPresence>(new TestPresence());
        var view = Render(open: true, offered: [SupportAssistance.None]);

        Assert.Empty(view.FindAll("[data-presence]"));
    }

    [Fact]
    public void AMessageToAPersonNobodyAnswers_TellsTheMemberAfterAMinute_UntilSupportAnswers()
    {
        var time = new ManualTime();
        Services.AddSingleton<TimeProvider>(time);
        var raised = Case("case-1", SupportAssistantState.None);
        Raises(raised, messages: Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        view.Find("[data-tab='support']").Click();
        Send(view, Question);
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-message]")));

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Empty(view.FindAll("[data-no-answer]"));

        time.Advance(TimeSpan.FromSeconds(2));
        view.WaitForAssertion(() => Assert.Equal(SupportTranscriptText.NoAnswerYet.Default, view.Find("[data-no-answer]").TextContent.Trim()));

        _cases.Setup(x => x.GetMessagesAsync(TeamKey, raised.Id, null, It.IsAny<int>(), default))
            .ReturnsAsync(new SupportMessagePage { Items = [Message(1, Question, SupportMessageKind.User), Message(2, "From support.", SupportMessageKind.User, author: "staff")] });
        _notifier.Raise(x => x.CaseUpdated += null, _notifier.Object, new SupportCaseUpdatedEventArgs { TeamKey = TeamKey, CaseId = raised.Id, Change = SupportCaseChange.Replied, FromChannel = false });

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-no-answer]")));
    }

    [Fact]
    public void WhileSupportIsAway_TheMemberIsToldAtOnce_ButNotWhileSomebodyWrites()
    {
        Services.AddSingleton<TimeProvider>(new ManualTime());
        Services.AddSingleton<ISupportPresence>(new TestPresence(SupportPresenceState.Away));
        var raised = Case("case-1", SupportAssistantState.None);
        Raises(raised, messages: Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);
        view.Find("[data-tab='support']").Click();
        Send(view, Question);

        view.WaitForAssertion(() => Assert.Equal(SupportTranscriptText.NoAnswerAway.Default, view.Find("[data-no-answer]").TextContent.Trim()));

        _typing.Writes(TeamKey, raised.Id, "staff", asSupport: true, name: "Sam");

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-no-answer]")));
    }

    [Fact]
    public void WithTheAssistant_NothingIsSaidAboutWaiting()
    {
        Services.AddSingleton<TimeProvider>(new ManualTime());
        Services.AddSingleton<ISupportPresence>(new TestPresence(SupportPresenceState.Away));
        Raises(Case("case-1"), messages: Message(1, Question, SupportMessageKind.User));
        var view = Render(open: true);

        Send(view, Question);

        view.WaitForAssertion(() => _cases.Verify(x => x.RunAssistantAsync(TeamKey, "case-1", It.IsAny<IProgress<SupportAssistantProgress>>(), default), Times.Once));
        Assert.Empty(view.FindAll("[data-no-answer]"));
        Assert.Empty(view.FindAll("[data-presence]"));
    }

    private sealed class SwitchableAvailability(SupportAssistance[] offered) : ISupportPanelAvailability
    {
        private SupportAssistance[] _offered = offered;

        public Task<SupportAssistance[]> GetOfferedAsync(string teamKey, CancellationToken cancellationToken = default) => Task.FromResult(_offered);

        public event EventHandler Changed;

        public void Switch(SupportAssistance[] offered)
        {
            _offered = offered;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
