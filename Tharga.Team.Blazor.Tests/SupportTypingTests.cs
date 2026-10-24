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
/// Flashing dots while the other side is writing: who is shown to whom, how long a signal lasts, how often a writer
/// reports, and the dots in a transcript.
/// </summary>
public class SupportTypingTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";
    private const string Author = "alice";
    private const string Staff = "staff";

    private readonly TestTypingNotifier _notifier = new();
    private readonly ManualTime _time = new();
    private int _changes;

    private SupportTypingWatch Watch(bool authorOnRight)
    {
        var watch = new SupportTypingWatch(_notifier, _time, () => _changes++);
        watch.Follow(TeamKey, CaseId, Author, authorOnRight);
        return watch;
    }

    [Fact]
    public void AMember_SeesSupportWriting_AndIsToldOnce()
    {
        using var watch = Watch(authorOnRight: true);

        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true, name: "Sam");
        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true, name: "Sam");

        var writer = Assert.Single(watch.Writing);
        Assert.Equal("Sam", writer.Name);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Support_SeesTheAuthorWriting()
    {
        using var watch = Watch(authorOnRight: false);

        _notifier.Writes(TeamKey, CaseId, Author, asSupport: false);

        Assert.Equal(Author, Assert.Single(watch.Writing).Identity);
    }

    /// <summary>The right of the transcript is the reader's own side, so nobody sees dots for themselves.</summary>
    [Fact]
    public void NobodySeesTheirOwnSideWriting()
    {
        using var member = Watch(authorOnRight: true);
        using var support = Watch(authorOnRight: false);

        _notifier.Writes(TeamKey, CaseId, Author, asSupport: false);
        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true);

        Assert.Equal(Staff, Assert.Single(member.Writing).Identity);
        Assert.Equal(Author, Assert.Single(support.Writing).Identity);
    }

    /// <summary>A developer trying the sample writes as the member in the panel and answers at the desk: each side shows the other.</summary>
    [Fact]
    public void ThePersonOnBothSides_SeesEachSideFromTheOther()
    {
        using var member = Watch(authorOnRight: true);
        using var support = Watch(authorOnRight: false);

        _notifier.Writes(TeamKey, CaseId, Author, asSupport: true);
        Assert.Single(member.Writing);
        Assert.Empty(support.Writing);

        _notifier.Writes(TeamKey, CaseId, Author, asSupport: false);
        Assert.True(Assert.Single(member.Writing).AsSupport);
        Assert.False(Assert.Single(support.Writing).AsSupport);
    }

    [Fact]
    public void WritingInAnotherCaseOrTeam_IsNotShown()
    {
        using var watch = Watch(authorOnRight: true);

        _notifier.Writes(TeamKey, "case-2", Staff, asSupport: true);
        _notifier.Writes("globex", CaseId, Staff, asSupport: true);

        Assert.Empty(watch.Writing);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void ASignal_Lapses_WhenNoFurtherOneFollows()
    {
        using var watch = Watch(authorOnRight: true);
        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true);

        _time.Advance(SupportTypingWatch.ShowFor);

        Assert.Empty(watch.Writing);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void FurtherSignals_KeepTheDotsShowing()
    {
        using var watch = Watch(authorOnRight: true);
        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true);

        _time.Advance(SupportTypingReporter.Interval);
        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true);
        _time.Advance(SupportTypingWatch.ShowFor - TimeSpan.FromSeconds(1));

        Assert.Single(watch.Writing);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void AMessageArriving_OrAnotherCase_EndsTheDots()
    {
        using var watch = Watch(authorOnRight: true);
        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true);

        watch.Clear();
        Assert.Empty(watch.Writing);

        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true);
        watch.Follow(TeamKey, "case-2", Author, authorOnRight: true);
        Assert.Empty(watch.Writing);
    }

    [Fact]
    public void ADisposedWatch_StopsListening()
    {
        var watch = Watch(authorOnRight: true);

        watch.Dispose();
        _notifier.Writes(TeamKey, CaseId, Staff, asSupport: true);

        Assert.Empty(watch.Writing);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public async Task AWriter_ReportsAtOnce_ThenOnceAnInterval()
    {
        var cases = new Mock<ISupportCaseService>();
        var reporter = new SupportTypingReporter(cases.Object, _time);

        reporter.Typed(TeamKey, CaseId, asSupport: true);
        reporter.Typed(TeamKey, CaseId, asSupport: true);
        _time.Advance(SupportTypingReporter.Interval);
        reporter.Typed(TeamKey, CaseId, asSupport: true);
        await reporter.SendingAsync();

        cases.Verify(x => x.ReportTypingAsync(TeamKey, CaseId, true, default), Times.Exactly(2));
    }

    [Fact]
    public async Task AfterSending_TheNextKeystrokeReportsAtOnce()
    {
        var cases = new Mock<ISupportCaseService>();
        var reporter = new SupportTypingReporter(cases.Object, _time);

        reporter.Typed(TeamKey, CaseId, asSupport: false);
        await reporter.SendingAsync();
        reporter.Typed(TeamKey, CaseId, asSupport: false);
        await reporter.SendingAsync();

        cases.Verify(x => x.ReportTypingAsync(TeamKey, CaseId, false, default), Times.Exactly(2));
    }

    [Fact]
    public async Task AConversationNotYetRaised_IsNotReported()
    {
        var cases = new Mock<ISupportCaseService>();
        var reporter = new SupportTypingReporter(cases.Object, _time);

        reporter.Typed(TeamKey, null, asSupport: false);
        await reporter.SendingAsync();

        cases.Verify(x => x.ReportTypingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), default), Times.Never);
    }

    /// <summary>The dots are a courtesy: a report the service refuses must not stop the message that follows.</summary>
    [Fact]
    public async Task ARefusedReport_IsDropped()
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.ReportTypingAsync(TeamKey, CaseId, true, default)).ThrowsAsync(new UnauthorizedAccessException());
        var reporter = new SupportTypingReporter(cases.Object, _time);

        reporter.Typed(TeamKey, CaseId, asSupport: true);

        Assert.Null(await Record.ExceptionAsync(() => reporter.SendingAsync()));
    }

    [Fact]
    public void ATranscript_ShowsAWriterAsDots_WhereTheirMessageWillBe()
    {
        var view = RenderTranscript(authorOnRight: true, Writer(Staff, asSupport: true, "Sam"));

        var row = view.Find("[data-writing]");
        Assert.Equal("support", row.GetAttribute("data-writing"));
        Assert.Equal("left", row.GetAttribute("data-side"));
        Assert.Equal(3, row.QuerySelectorAll(".support-typing__dot").Length);
        Assert.Equal("Sam is typing…", row.QuerySelector(".support-typing__label")!.TextContent);
        Assert.Equal("status", row.QuerySelector("[data-typing]")!.GetAttribute("role"));
    }

    [Fact]
    public void ATranscript_ShowsTheAnswerBeingWritten_OnTheAssistantsSide()
    {
        AddServices(new Mock<ISupportCaseService>());

        var view = Render<SupportTranscript>(p => p
            .Add(x => x.Messages, [])
            .Add(x => x.CaseAuthorIdentity, Author)
            .Add(x => x.AuthorOnRight, true)
            .Add(x => x.AssistantProgress, new SupportAssistantProgress { AnswerSoFar = "Check the date" }));

        var row = view.Find("[data-answer-so-far]");
        Assert.Equal("left", row.GetAttribute("data-side"));
        Assert.Equal("true", row.GetAttribute("aria-busy"));
        Assert.NotNull(row.QuerySelector("[data-sender='assistant']"));
        Assert.Contains("Check the date", row.TextContent);
    }

    private static SupportAssistantCall Call(string name, string arguments = "") => new() { Name = name, Arguments = arguments };

    [Fact]
    public void ATranscript_SaysWhichLookupTheAssistantIsOn_BesideItsDots()
    {
        AddServices(new Mock<ISupportCaseService>());

        var view = Render<SupportTranscript>(p => p
            .Add(x => x.Messages, [])
            .Add(x => x.CaseAuthorIdentity, Author)
            .Add(x => x.AuthorOnRight, true)
            .Add(x => x.ThinkingText, "Looking into it")
            .Add(x => x.AssistantProgress, new SupportAssistantProgress { Round = 2, Tools = ["get_case_messages"], Calls = [Call("get_my_cases"), Call("get_case_messages")] }));

        Assert.Equal("thinking", view.Find("[data-answer-so-far]").GetAttribute("data-answer-so-far"));
        Assert.Equal("Second lookup…", view.Find("[data-answer-so-far] .support-typing__label").TextContent);
        Assert.Empty(view.FindAll("[data-step]"));
    }

    [Theory]
    [InlineData(1, "First lookup…")]
    [InlineData(3, "Third lookup…")]
    [InlineData(10, "Tenth lookup…")]
    [InlineData(11, "Lookup 11…")]
    public async Task Lookups_AreCountedInWords_ThenInNumbers(int made, string expected)
    {
        var text = await new DefaultThargaTextProvider().ResolveAsync(SupportTranscriptText.All);
        var progress = new SupportAssistantProgress { Tools = ["get_my_cases"], Calls = [.. Enumerable.Range(0, made).Select(_ => Call("get_my_cases"))] };

        Assert.Equal(expected, SupportAssistantSteps.Describe(text, progress));
    }

    /// <summary>While the model thinks or writes, the dots keep their own words.</summary>
    [Fact]
    public async Task WithNoLookupRunning_NothingIsSaid()
    {
        var text = await new DefaultThargaTextProvider().ResolveAsync(SupportTranscriptText.All);

        Assert.Null(SupportAssistantSteps.Describe(text, null));
        Assert.Null(SupportAssistantSteps.Describe(text, new SupportAssistantProgress { Calls = [Call("get_my_cases")] }));
    }

    [Fact]
    public void AnAnswer_ShowsItsServiceCalls_Folded_WithWhatEachWasAskedFor()
    {
        AddServices(new Mock<ISupportCaseService>());

        var view = Render<SupportTranscript>(p => p
            .Add(x => x.Messages,
            [
                new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = Author, AuthorName = "Alice", Body = "Where is my export case?", SentAt = DateTime.UtcNow },
                new SupportMessage
                {
                    Sequence = 2, Kind = SupportMessageKind.Assistant, AuthorName = "Assistant", Body = "Here it is.", SentAt = DateTime.UtcNow,
                    AssistantCalls = [Call("get_my_cases"), Call("search_my_cases", "text: \"export\"")]
                }
            ])
            .Add(x => x.CaseAuthorIdentity, Author)
            .Add(x => x.AuthorOnRight, true));

        var calls = view.Find("[data-calls='2']");
        Assert.False(calls.HasAttribute("open"));
        Assert.Equal("2 service calls", calls.QuerySelector("summary")!.TextContent.Trim());
        Assert.Equal(["get_my_cases()", "search_my_cases(text: \"export\")"], view.FindAll("[data-calls='2'] [data-call]").Select(x => x.TextContent.Trim()));
        Assert.Single(view.FindAll("[data-calls]"));
    }

    [Fact]
    public async Task OneServiceCall_IsCountedAsOne()
    {
        var text = await new DefaultThargaTextProvider().ResolveAsync(SupportTranscriptText.All);

        Assert.Equal("1 service call", SupportAssistantSteps.Counted(text, [Call("get_my_cases")]));
    }

    [Fact]
    public void AWriterWithNoName_IsReadOutAsSupport()
    {
        var view = RenderTranscript(authorOnRight: true, Writer(Staff, asSupport: true, null));

        Assert.Equal("Support is typing…", view.Find(".support-typing__label").TextContent);
    }

    [Fact]
    public void TheAuthorWriting_IsShownToSupport_OnTheAuthorsSide()
    {
        var view = RenderTranscript(authorOnRight: false, Writer(Author, asSupport: false, "Alice"));

        Assert.Equal("author", view.Find("[data-writing]").GetAttribute("data-writing"));
        Assert.Equal("left", view.Find("[data-writing]").GetAttribute("data-side"));
    }

    /// <summary>At the desk: the member writing shows in the open conversation, and their message arriving ends it.</summary>
    [Fact]
    public void AnOpenConversation_ShowsTheMemberWriting_UntilTheirMessageArrives()
    {
        var supportCase = new SupportCase
        {
            Id = CaseId,
            TeamKey = TeamKey,
            AuthorIdentity = Author,
            AuthorName = "Alice",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
            MessageCount = 1
        };
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetMessagesAsync(TeamKey, CaseId, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportMessagePage { Items = [] });
        AddServices(cases);
        var view = Render<SupportCaseConversation>(p => p.Add(x => x.Case, supportCase).Add(x => x.AsStaff, true));

        _notifier.Writes(TeamKey, CaseId, Author, asSupport: false, name: "Alice");
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-writing='author']")));

        view.Render(p => p.Add(x => x.Case, supportCase with { MessageCount = 2 }));
        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-writing]")));
    }

    [Fact]
    public async Task SupportWritingAnAnswer_IsReportedAsSupport()
    {
        var cases = new Mock<ISupportCaseService>();
        var view = RenderConversation(cases, asStaff: true, SupportAssistantState.None);

        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>Try</p>"));

        cases.Verify(x => x.ReportTypingAsync(TeamKey, CaseId, true, default), Times.Once);
    }

    [Fact]
    public async Task AMemberWritingToTheAssistant_TellsNobody()
    {
        var cases = new Mock<ISupportCaseService>();
        var view = RenderConversation(cases, asStaff: false, SupportAssistantState.Active);

        await view.InvokeAsync(() => view.FindComponent<RichTextEditor>().Instance.ValueChanged.InvokeAsync("<p>And</p>"));

        cases.Verify(x => x.ReportTypingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), default), Times.Never);
    }

    private IRenderedComponent<SupportCaseConversation> RenderConversation(Mock<ISupportCaseService> cases, bool asStaff, SupportAssistantState state)
    {
        var supportCase = new SupportCase
        {
            Id = CaseId,
            TeamKey = TeamKey,
            AuthorIdentity = Author,
            AuthorName = "Alice",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
            MessageCount = 1,
            AssistantState = state
        };
        cases.Setup(x => x.GetMessagesAsync(TeamKey, CaseId, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportMessagePage { Items = [] });
        AddServices(cases);

        return Render<SupportCaseConversation>(p => p.Add(x => x.Case, supportCase).Add(x => x.AsStaff, asStaff));
    }

    /// <summary>The team's own queue: the member writing shows, and what they sent then appears in the open conversation.</summary>
    [Fact]
    public async Task TheTeamsQueue_ShowsTheMemberWriting_AndThenWhatTheySent()
    {
        var supportCase = new SupportCase
        {
            Id = CaseId,
            TeamKey = TeamKey,
            AuthorIdentity = Author,
            AuthorName = "Alice",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
            MessageCount = 1
        };
        var asked = new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = Author, AuthorName = "Alice", Body = "The export is empty.", SentAt = supportCase.CreatedAt };
        var more = asked with { Sequence = 2, Body = "Since Monday." };

        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);
        var caseNotifier = new Mock<ISupportCaseNotifier>();
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetCasesAsync(TeamKey, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportCasePage { Items = [supportCase] });
        cases.Setup(x => x.GetCaseAsync(TeamKey, CaseId, default)).ReturnsAsync(supportCase);
        cases.Setup(x => x.GetMessagesAsync(TeamKey, CaseId, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportMessagePage { Items = [asked] });
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton(caseNotifier.Object);
        AddServices(cases);
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        var view = Render<SupportQueueView>();
        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Export is empty")).Click());

        _notifier.Writes(TeamKey, CaseId, Author, asSupport: false, name: "Alice");
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-writing='author']")));

        cases.Setup(x => x.GetMessagesAsync(TeamKey, CaseId, null, It.IsAny<int>(), default)).ReturnsAsync(new SupportMessagePage { Items = [asked, more] });
        caseNotifier.Raise(x => x.CaseUpdated += null, caseNotifier.Object, new SupportCaseUpdatedEventArgs { TeamKey = TeamKey, CaseId = CaseId, Change = SupportCaseChange.Replied, FromChannel = false });

        view.WaitForAssertion(() => Assert.Contains("Since Monday.", view.Markup));
        Assert.Empty(view.FindAll("[data-writing]"));
    }

    private static SupportCaseTypingEventArgs Writer(string identity, bool asSupport, string name) => new()
    {
        TeamKey = TeamKey,
        CaseId = CaseId,
        Identity = identity,
        Name = name,
        AsSupport = asSupport
    };

    private IRenderedComponent<SupportTranscript> RenderTranscript(bool authorOnRight, params SupportCaseTypingEventArgs[] writing)
    {
        AddServices(new Mock<ISupportCaseService>());

        return Render<SupportTranscript>(p => p
            .Add(x => x.Messages, [])
            .Add(x => x.CaseAuthorIdentity, Author)
            .Add(x => x.AuthorOnRight, authorOnRight)
            .Add(x => x.TeamKey, TeamKey)
            .Add(x => x.CaseId, CaseId)
            .Add(x => x.Writing, writing));
    }

    private void AddServices(Mock<ISupportCaseService> cases)
    {
        Services.AddSingleton(cases.Object);
        Services.AddSingleton<ISupportTypingNotifier>(_notifier);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
