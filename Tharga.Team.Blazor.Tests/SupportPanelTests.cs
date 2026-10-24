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
        bool withTeam = true)
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
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        return Render<SupportPanel>(p => p.Add(x => x.Offered, offered));
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
        view.Find("input[data-draft]").Input(text);
        view.Find("[data-send]").Click();
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

        view.WaitForAssertion(() => Assert.Equal("Working 1…", view.Find("[data-working] .support-typing__label--shown").TextContent));

        SupportAssistantCall[] made = [new() { Name = "get_my_cases" }, new() { Name = "search_my_cases", Arguments = "text: \"export\"" }];
        view.InvokeAsync(() => report.Report(new SupportAssistantProgress { Round = 2, AnswerSoFar = "You have two", Tools = ["search_my_cases"], Calls = made }));

        view.WaitForAssertion(() => Assert.Equal("Working 2…", view.Find("[data-answer-so-far] [data-step]").TextContent));

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
            o.Support.ServiceCallsShownTo = SupportServiceCallsAudience.Support;
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
        Assert.Equal("Working…", view.Find("[data-working] .support-typing__label--shown").TextContent);
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

        view.Find("input[data-draft]").Input("And the");
        view.Find("input[data-draft]").Input("And the date");

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

        view.Find("input[data-draft]").Input("And the date");

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
