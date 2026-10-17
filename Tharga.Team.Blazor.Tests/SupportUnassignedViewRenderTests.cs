using System.Globalization;
using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Blazor;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The unassigned queue: reading a case that belongs to no team, and giving it one.
/// </summary>
/// <remarks>
/// <b>Rendered rather than asserted through the service, for the reason the sibling component taught.</b> Its
/// case subject was a <c>RadzenLink</c> with a <c>Click</c> handler — a parameter that does not exist — so
/// Blazor swallowed the handler as an unmatched attribute and shipped a dead control. It compiled and every
/// unit test passed. Only rendering the markup and clicking the thing a person clicks proves the wiring is
/// there.
/// </remarks>
public class SupportUnassignedViewRenderTests : BunitContext
{
    private const string OwnStorageKey = "Tharga.Team.Support.Unassigned.Filter";

    private static readonly DateTime Morning = new(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);

    private static readonly SupportCase Case = new()
    {
        Id = "case-1",
        TeamKey = null,
        AuthorIdentity = null,
        AuthorName = "stranger@example.com",
        Subject = "Cannot sign in",
        Status = SupportCaseStatus.Open,
        CreatedAt = Morning,
        MessageCount = 1
    };

    private static readonly SupportMessage Arrived = new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorIdentity = null,
        AuthorName = "stranger@example.com",
        Body = "It says my key expired.",
        SentAt = Morning,
        Source = SupportChannelType.Email
    };

    [Fact]
    public void TheQueueRendersWhatArrivedWithNoTeam()
    {
        var view = RenderView(out _);

        Assert.Contains("Cannot sign in", view.Markup);
        Assert.Contains("stranger@example.com", view.Find("[data-case='case-1'] [data-sender]").TextContent);
        Assert.Contains("Open", view.Find("[data-status='case-1']").TextContent);
    }

    [Fact]
    public void EachCaseIsACard_ClosedUntilItIsOpened_AndReadsNothingMeanwhile()
    {
        var view = RenderView(out var cases);

        Assert.Single(view.FindComponents<ExpandableCard>());
        Assert.DoesNotContain("It says my key expired.", view.Markup);
        Assert.Empty(view.FindAll("[data-conversation]"));
        cases.Verify(x => x.GetMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public void OpeningTheCard_ShowsItsTranscriptInsideIt()
    {
        var view = RenderView(out _);

        Open(view, Case.Id);

        Assert.Contains("It says my key expired.", view.Find("[data-case='case-1'] [data-conversation='case-1']").TextContent);

        // Provenance, which is the reason an agent looks at an unassigned case differently: it was
        // attributed on a From header rather than by an authenticated caller.
        Assert.Contains("Email", view.Markup);
    }

    [Fact]
    public void AMailedCase_SaysSoByItsIcon()
    {
        var view = RenderView(out _, items: [Case with { Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "<m1@example.com>" }] }]);

        Assert.Equal("mail", view.FindComponent<ExpandableCard>().Instance.Icon);
    }

    [Fact]
    public void TheQueueCarriesNoHeading()
    {
        var view = RenderView(out _);

        Assert.DoesNotContain("Unassigned support cases", view.Markup);
        Assert.Contains("arrived without a team", view.Markup);
    }

    [Fact]
    public void EveryDate_GoesThroughTheHostsTimeTemplate()
    {
        var view = RenderView(out _, timeTemplate: HostTime);

        Assert.Equal(HostTimeText(Morning), view.Find("[data-time='case-1'] .host-time").TextContent);
        Assert.DoesNotContain(Morning.ToLocalTime().ToString("g"), view.Markup);

        Open(view, Case.Id);

        Assert.Equal(HostTimeText(Morning), view.Find("[data-conversation='case-1'] .host-time").TextContent);
        Assert.DoesNotContain(Morning.ToLocalTime().ToString("g"), view.Markup);
    }

    [Fact]
    public void WithoutATimeTemplate_TheLocalShortDateAndTimeIsShown()
    {
        var view = RenderView(out _);

        Assert.Contains(Morning.ToLocalTime().ToString("g"), view.Find("[data-time='case-1']").TextContent);
    }

    [Fact]
    public void TheCaseShowsWhenItLastMoved()
    {
        var later = Morning.AddHours(3);
        var view = RenderView(out _, items: [Case with { LastMessageAt = later }], timeTemplate: HostTime);

        Assert.Equal(HostTimeText(later), view.Find("[data-time='case-1'] .host-time").TextContent);
    }

    [Fact]
    public void ByDefault_OpenCasesAreShown_WaitingFirst_ThenTheLiveliest()
    {
        var view = RenderView(out _, items: Mixed);

        Assert.Equal(["waiting", "newest", "older"], CaseIds(view));
    }

    [Fact]
    public async Task ChoosingClosed_ShowsTheClosedOnes()
    {
        var view = RenderView(out _, items: Mixed);

        await ChooseStatusesAsync(view, SupportCaseStatus.Closed);

        Assert.Equal(["closed"], CaseIds(view));
    }

    [Fact]
    public async Task ChoosingNoStatus_ShowsEveryCase()
    {
        var view = RenderView(out _, items: Mixed);

        await ChooseStatusesAsync(view);

        Assert.Equal(4, CaseIds(view).Length);
    }

    [Fact]
    public void ThereIsNoKindOfConversationToChoose_OnlyStatusAndSearch()
    {
        var view = RenderView(out _);

        Assert.Empty(view.FindComponents<RadzenSelectBar<IEnumerable<SupportAssistance>>>());
        Assert.Single(view.FindComponents<RadzenSelectBar<IEnumerable<SupportCaseStatus>>>());
        Assert.Single(view.FindAll("input[data-search]"));
    }

    [Fact]
    public void Searching_NarrowsTheListToTheCasesThatMatch()
    {
        var view = RenderView(out _, items: Mixed);

        view.Find("input[data-search]").Input("invoice");

        Assert.Equal(["newest"], CaseIds(view));
    }

    /// <summary>The address a mail came from and the teams it suggests are what an operator remembers a case by.</summary>
    [Theory]
    [InlineData("pat@contoso.example")]
    [InlineData("globex")]
    [InlineData("STRANGER signed")]
    public void Searching_FindsTheSender_TheSuggestedTeams_AndEveryWordSomewhere(string search)
    {
        var mailed = Case with
        {
            Id = "mailed",
            Subject = "Signed out again",
            AuthorName = "Pat Stranger",
            Bindings = [new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = "<m1@example.com>", Address = "pat@contoso.example" }],
            SuggestedTeams = [new SupportCaseTeam { Key = "globex", Name = "Globex" }],
            SuggestionReason = SupportTeamSuggestionReason.SeveralTeams
        };

        var view = RenderView(out _, items: [mailed, Case with { Id = "other", Subject = "Billing", AuthorName = "someone@example.com" }]);

        view.Find("input[data-search]").Input(search);

        Assert.Equal(["mailed"], CaseIds(view));
    }

    [Fact]
    public void ASearchMatchingNothing_SaysSo()
    {
        var view = RenderView(out _);

        view.Find("input[data-search]").Input("nothing like it");

        Assert.Empty(CaseIds(view));
        Assert.Contains("No case matches the filter.", view.Markup);
    }

    [Fact]
    public void ClearingTheSearch_BringsEveryCaseBack()
    {
        var view = RenderView(out _, items: Mixed);

        view.Find("input[data-search]").Input("invoice");
        view.Find("input[data-search]").Input(string.Empty);

        Assert.Equal(3, CaseIds(view).Length);
    }

    [Fact]
    public async Task TheFilterIsRemembered_UnderTheViewsOwnKey()
    {
        var view = RenderView(out _, items: Mixed);

        await ChooseStatusesAsync(view, SupportCaseStatus.Closed);

        var stored = JSInterop.VerifyInvoke("localStorage.setItem").Arguments;
        Assert.Equal(OwnStorageKey, stored[0]);
        Assert.Contains("Closed", stored[1]?.ToString());
    }

    [Fact]
    public void ARememberedFilter_IsRestoredFromTheViewsOwnKey()
    {
        JSInterop.Setup<string>("localStorage.getItem", x => (string)x.Arguments[0] == OwnStorageKey)
            .SetResult("{\"Statuses\":[\"Closed\"],\"Kinds\":[\"None\"]}");

        var view = RenderView(out _, items: Mixed);

        view.WaitForAssertion(() => Assert.Equal(["closed"], CaseIds(view)));
    }

    [Fact]
    public async Task AHostsOwnStorageKey_IsTheOneUsed()
    {
        var view = RenderView(out _, items: Mixed, storageKey: "host.unassigned");

        await ChooseStatusesAsync(view, SupportCaseStatus.Closed);

        Assert.Equal("host.unassigned", JSInterop.VerifyInvoke("localStorage.setItem").Arguments[0]);
    }

    [Fact]
    public void TheSearchIsNotRemembered()
    {
        var view = RenderView(out _, items: Mixed);

        view.Find("input[data-search]").Input("invoice");

        Assert.DoesNotContain(JSInterop.Invocations, x => x.Identifier == "localStorage.setItem");
    }

    [Fact]
    public void ItShowsHowManyAreAwaitingAnAnswer_AndMarksEach()
    {
        var view = RenderView(out _, items: Mixed, awaiting: 1);

        Assert.Contains("1 awaiting an answer", view.Markup);
        Assert.Equal("waiting", view.Find("[data-needs-answer]").GetAttribute("data-needs-answer"));
    }

    /// <summary>A queue that stopped at its first page told nobody about the cases it never listed.</summary>
    [Fact]
    public void TheQueueReadsEveryPage()
    {
        var cases = Cases([]);
        cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default))
            .ReturnsAsync(new SupportCasePage { Items = [Case with { Id = "first" }], NextCursor = "next" });
        cases.Setup(x => x.GetUnassignedCasesAsync("next", 100, default))
            .ReturnsAsync(new SupportCasePage { Items = [Case with { Id = "second", AwaitingSupport = true }] });

        var view = Render(cases);

        Assert.Equal(["second", "first"], CaseIds(view));
    }

    /// <summary>A change made on another instance reaches no in-process notifier, so the host asks for the re-read.</summary>
    [Fact]
    public async Task RefreshingReReadsTheQueue()
    {
        var view = RenderView(out var cases);

        cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default))
            .ReturnsAsync(new SupportCasePage { Items = [Case, Case with { Id = "case-new", Subject = "Arrived elsewhere" }] });
        cases.Setup(x => x.GetAwaitingUnassignedCountAsync(default)).ReturnsAsync(2);

        await view.Instance.RefreshAsync();

        Assert.Contains("Arrived elsewhere", view.Markup);
        Assert.Contains("2 awaiting an answer", view.Markup);
    }

    [Fact]
    public void ACaseGivenATeamElsewhereInThisProcess_LeavesTheQueue()
    {
        var notifier = new Mock<ISupportCaseNotifier>();
        var view = RenderView(out var cases, notifier: notifier);

        cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = [] });
        notifier.Raise(x => x.CaseUpdated += null, new SupportCaseUpdatedEventArgs { TeamKey = "acme", CaseId = Case.Id, Change = SupportCaseChange.Assigned, FromChannel = false });

        view.WaitForAssertion(() => Assert.Empty(CaseIds(view)));
    }

    [Fact]
    public void AnsweringInsideTheCard_AnswersTheCaseWithNoTeam()
    {
        var view = RenderView(out var cases);

        Open(view, Case.Id);
        view.TypeRichText("<p>Which organisation are you with?</p>");
        view.Find("[data-answer='case-1']").Click();

        cases.Verify(x => x.AnswerCaseAsync(null, Case.Id, string.Empty, "<p>Which organisation are you with?</p>", It.Is<IReadOnlyList<SupportAttachmentUpload>>(f => f.Count == 0), default), Times.Once);
    }

    [Fact]
    public void ClosingInsideTheCard_ClosesTheCase()
    {
        var view = RenderView(out var cases);

        Open(view, Case.Id);
        view.Find("[data-close='case-1']").Click();

        cases.Verify(x => x.CloseCaseAsync(null, Case.Id, default), Times.Once);
    }

    [Fact]
    public async Task AClosedCase_IsReopenedInsideItsCard()
    {
        var closed = Case with { Status = SupportCaseStatus.Closed, ClosedAt = Morning.AddDays(1), ClosedBy = "support" };
        var view = RenderView(out var cases, items: [closed]);

        await ChooseStatusesAsync(view, SupportCaseStatus.Closed);
        Open(view, Case.Id);

        Assert.Empty(view.FindAll("[data-close='case-1']"));
        view.Find("[data-reopen='case-1']").Click();

        cases.Verify(x => x.ReopenCaseAsync(null, Case.Id, default), Times.Once);
    }

    [Fact]
    public void AnAnswerInsideTheCard_ReadsTheTranscriptAgain()
    {
        var view = RenderView(out var cases);

        Open(view, Case.Id);
        view.TypeRichText("On it.");
        view.Find("[data-answer='case-1']").Click();

        cases.Verify(x => x.GetMessagesAsync(null, Case.Id, null, SupportCaseLimits.MaxMessagesPerCase, default), Times.Exactly(2));
    }

    /// <summary>
    /// The whole point of the component: an operator who knows whose case it is gives it a team.
    /// </summary>
    [Fact]
    public async Task ChoosingATeamAndAssigning_AssignsTheCase()
    {
        var view = RenderView(out var cases);

        Open(view, Case.Id);

        var dropdown = view.FindComponent<RadzenDropDown<string>>();
        await view.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync("acme"));

        view.FindAll("button").First(b => b.TextContent.Trim() == "Assign").Click();

        cases.Verify(x => x.AssignCaseAsync("case-1", "acme", default), Times.Once);
    }

    [Fact]
    public async Task AnAssignedCase_LeavesTheQueue()
    {
        var view = RenderView(out var cases);

        Open(view, Case.Id);
        cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default)).ReturnsAsync(new SupportCasePage { Items = [] });

        var dropdown = view.FindComponent<RadzenDropDown<string>>();
        await view.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync("acme"));
        view.FindAll("button").First(b => b.TextContent.Trim() == "Assign").Click();

        Assert.Empty(CaseIds(view));
        Assert.DoesNotContain("assigned this case first", view.Markup);
    }

    /// <summary>
    /// Losing the race must say so. A queue that silently swallows the second click is how an operator stops
    /// trusting it.
    /// </summary>
    [Fact]
    public async Task AssigningACaseSomebodyElseTook_SaysSo()
    {
        var view = RenderView(out _, assignSucceeds: false);

        Open(view, Case.Id);

        var dropdown = view.FindComponent<RadzenDropDown<string>>();
        await view.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync("acme"));

        view.FindAll("button").First(b => b.TextContent.Trim() == "Assign").Click();

        Assert.Contains("assigned this case first", view.Markup);
    }

    /// <summary>
    /// Reading the queue and listing teams are separate grants, so an operator may hold one and not the
    /// other. That is a smaller capability rather than a broken one — they can still answer and close.
    /// </summary>
    [Fact]
    public void WithoutCrossTeamDiscovery_ThereIsNothingToAssignToAndItSaysWhy()
    {
        var view = RenderView(out _, withOversight: false);

        Open(view, Case.Id);

        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent.Trim() == "Assign");
        Assert.Contains("cross-team read grant", view.Markup);
        Assert.Single(view.FindAll("[data-close='case-1']"));
    }

    /// <summary>
    /// A refused read renders a note rather than taking the page down: this is a panel beside content the
    /// operator can see.
    /// </summary>
    [Fact]
    public void WithoutTheSystemGrant_ItRendersANoteRatherThanThrowing()
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default))
            .ThrowsAsync(new UnauthorizedAccessException("no grant"));

        var view = Render(cases);

        Assert.Contains("do not have access", view.Markup);
        Assert.DoesNotContain("Cannot sign in", view.Markup);
    }

    /// <summary>
    /// The teams the sender matched are offered with why none was chosen, and one click gives the case one of
    /// them through the same operation the dropdown uses.
    /// </summary>
    [Fact]
    public void ASuggestedTeam_IsOfferedWithItsReasonInsideTheCard_AndAssignsInOneClick()
    {
        var view = RenderView(out var cases, items: [Suggesting(SupportTeamSuggestionReason.SenderNotVerified, "acme")]);

        Assert.DoesNotContain("Suggested teams", view.Markup);

        Open(view, Case.Id);

        var suggestions = view.Find("[data-case='case-1'] [data-suggestions='case-1']");
        Assert.Contains("Suggested teams", suggestions.TextContent);
        Assert.Contains("could not be verified", suggestions.TextContent);

        view.FindAll("button").First(b => b.TextContent.Trim() == "Assign to Acme").Click();

        cases.Verify(x => x.AssignCaseAsync("case-1", "acme", default), Times.Once);
    }

    [Theory]
    [InlineData(SupportTeamSuggestionReason.SeveralTeams, "more than one team")]
    [InlineData(SupportTeamSuggestionReason.SharedAddress, "More than one user account")]
    public void EachReason_IsSaidInWords(SupportTeamSuggestionReason reason, string expected)
    {
        var view = RenderView(out _, items: [Suggesting(reason, "acme")]);

        Open(view, Case.Id);

        Assert.Contains(expected, view.Markup);
    }

    /// <summary>
    /// <b>Only a team that still exists can be assigned</b>, for the reason the dropdown lists teams rather than
    /// taking a key: a team deleted since the mail arrived would take the case into nobody's queue.
    /// </summary>
    [Fact]
    public void ASuggestedTeamThatIsNotListed_IsNamedButNotOffered()
    {
        var view = RenderView(out _, items: [Suggesting(SupportTeamSuggestionReason.SeveralTeams, "gone")]);

        Open(view, Case.Id);

        Assert.Contains("Gone Ltd", view.Markup);
        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent.Contains("Gone Ltd"));
    }

    [Fact]
    public void ACaseWithNoSuggestions_ShowsNone()
    {
        var view = RenderView(out _);

        Open(view, Case.Id);

        Assert.DoesNotContain("Suggested teams", view.Markup);
    }

    private static readonly SupportCase[] Mixed =
    [
        Case with { Id = "older", Subject = "Older question", LastMessageAt = Morning.AddMinutes(10) },
        Case with { Id = "closed", Subject = "Closed question", Status = SupportCaseStatus.Closed, LastMessageAt = Morning.AddMinutes(50) },
        Case with { Id = "newest", Subject = "Invoice is missing", LastMessageAt = Morning.AddMinutes(40) },
        Case with { Id = "waiting", Subject = "Still waiting", LastMessageAt = Morning.AddMinutes(5), AwaitingSupport = true }
    ];

    private static readonly RenderFragment<DateTime> HostTime = date => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "host-time");
        builder.AddContent(2, HostTimeText(date));
        builder.CloseElement();
    };

    private static string HostTimeText(DateTime date) => "host " + date.ToString("O", CultureInfo.InvariantCulture);

    private static SupportCase Suggesting(SupportTeamSuggestionReason reason, string teamKey) => Case with
    {
        SuggestedTeams = [new SupportCaseTeam { Key = teamKey, Name = teamKey == "acme" ? "Acme (then)" : "Gone Ltd" }],
        SuggestionReason = reason
    };

    private static string[] CaseIds(IRenderedComponent<SupportUnassignedView> view)
        => view.FindAll("[data-case]").Select(x => x.GetAttribute("data-case")).ToArray();

    private static void Open(IRenderedComponent<SupportUnassignedView> view, string caseId)
        => view.Find($"[data-case='{caseId}'] [style*='cursor: pointer']").Click();

    private static Task ChooseStatusesAsync(IRenderedComponent<SupportUnassignedView> view, params SupportCaseStatus[] statuses)
    {
        var status = view.FindComponent<RadzenSelectBar<IEnumerable<SupportCaseStatus>>>();
        return view.InvokeAsync(() => status.Instance.Change.InvokeAsync(statuses));
    }

    private static Mock<ISupportCaseService> Cases(SupportCase[] items, bool assignSucceeds = true, int awaiting = 0)
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetUnassignedCasesAsync(null, 100, default))
            .ReturnsAsync(new SupportCasePage { Items = items });
        cases.Setup(x => x.GetAwaitingUnassignedCountAsync(default)).ReturnsAsync(awaiting);
        foreach (var item in items)
        {
            cases.Setup(x => x.GetMessagesAsync(null, item.Id, null, SupportCaseLimits.MaxMessagesPerCase, default))
                .ReturnsAsync(new SupportMessagePage { Items = [Arrived] });
        }

        cases.Setup(x => x.AssignCaseAsync(It.IsAny<string>(), It.IsAny<string>(), default)).ReturnsAsync(assignSucceeds);

        return cases;
    }

    private IRenderedComponent<SupportUnassignedView> RenderView(
        out Mock<ISupportCaseService> cases,
        bool withOversight = true,
        bool assignSucceeds = true,
        SupportCase[] items = null,
        int awaiting = 0,
        RenderFragment<DateTime> timeTemplate = null,
        string storageKey = null,
        Mock<ISupportCaseNotifier> notifier = null)
    {
        cases = Cases(items ?? [Case], assignSucceeds, awaiting);

        return Render(cases, withOversight, timeTemplate, storageKey, notifier);
    }

    private IRenderedComponent<SupportUnassignedView> Render(
        Mock<ISupportCaseService> cases,
        bool withOversight = true,
        RenderFragment<DateTime> timeTemplate = null,
        string storageKey = null,
        Mock<ISupportCaseNotifier> notifier = null)
    {
        Services.AddSingleton(cases.Object);
        Services.AddSingleton((notifier ?? new Mock<ISupportCaseNotifier>()).Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();

        if (withOversight) Services.AddSingleton(Oversight());

        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", true));

        return Render<SupportUnassignedView>(p =>
        {
            p.Add(x => x.TimeTemplate, timeTemplate);
            if (storageKey != null) p.Add(x => x.StorageKey, storageKey);
        });
    }

    private static ITeamOversightService Oversight()
    {
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns("acme");
        team.SetupGet(x => x.Name).Returns("Acme");

        var oversight = new Mock<ITeamOversightService>();
        oversight.Setup(x => x.GetAllTeamsAsync()).Returns(One(team.Object));

        return oversight.Object;
    }

    private static async IAsyncEnumerable<ITeam> One(ITeam team)
    {
        yield return team;

        await Task.CompletedTask;
    }
}
