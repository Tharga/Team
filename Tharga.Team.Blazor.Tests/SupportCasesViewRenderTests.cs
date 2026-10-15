using Blazored.LocalStorage;
using Bunit;
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
/// Opening a case shows its conversation.
/// </summary>
/// <remarks>
/// <b>This exists because the first version could not do it, and nothing caught that.</b> The case subject
/// was a <c>RadzenLink</c> with a <c>Click</c> handler — and <c>RadzenLink</c> has no <c>Click</c> parameter,
/// so Blazor captured it as an unmatched HTML attribute and rendered a dead one. It compiled, every unit test
/// passed, the surface guard passed, and the component shipped unable to show a transcript.
/// <para>
/// <b>No assertion about the service could have found it.</b> The handler was never wired, so only rendering
/// the markup and clicking the thing a person clicks proves the wiring exists. That is the whole value of a
/// render test here, and why it is worth the fixture cost.
/// </para>
/// </remarks>
public class SupportCasesViewRenderTests : BunitContext
{
    private const string TeamKey = "acme";

    private static readonly SupportCase Case = new()
    {
        Id = "case-1",
        TeamKey = TeamKey,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    private static readonly SupportMessage Reply = new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "support",
        AuthorName = "Support",
        Body = "We are looking into the export.",
        SentAt = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void TheCaseListRenders()
    {
        var view = RenderView();

        Assert.Contains("Export is empty", view.Markup);
    }

    /// <summary>
    /// The transcript must not be on the page until the case is opened, or "it renders" would pass on a
    /// component that simply shows everything.
    /// </summary>
    [Fact]
    public void TheConversationIsHiddenUntilTheCaseIsOpened()
    {
        var view = RenderView();

        Assert.DoesNotContain("We are looking into the export.", view.Markup);
    }

    [Fact]
    public void ClickingTheCaseShowsItsConversation()
    {
        var view = RenderView();

        Open(view, "Export is empty");

        Assert.Contains("We are looking into the export.", view.Markup);
    }

    /// <summary>Clicking the open case again puts the transcript away.</summary>
    [Fact]
    public void ClickingItAgainClosesTheConversation()
    {
        var view = RenderView();

        Open(view, "Export is empty");
        Assert.Contains("We are looking into the export.", view.Markup);

        Open(view, "Export is empty");
        Assert.DoesNotContain("We are looking into the export.", view.Markup);
    }

    [Fact]
    public void WithNoVisibilityOnTheCase_ThereIsNothingToShare()
    {
        var view = RenderView();

        Open(view, "Export is empty");

        Assert.DoesNotContain("Who can read this case", view.Markup);
    }

    [Fact]
    public async Task TheAuthorSharesAPrivateCaseWithTheirTeam()
    {
        var view = RenderView(out var cases, SupportCaseVisibility.Private);

        Open(view, "Export is empty");

        Assert.Contains("Who can read this case", view.Markup);

        var share = view.FindComponent<SupportCaseVisibilityPicker>();
        await view.InvokeAsync(() => share.Instance.ValueChanged.InvokeAsync(SupportCaseVisibility.Team));

        cases.Verify(x => x.SetCaseVisibilityAsync(TeamKey, Case.Id, SupportCaseVisibility.Team, default), Times.Once);
    }

    /// <summary>Once somebody else in the team has written in it, only you is not on offer.</summary>
    [Fact]
    public void OnlyYou_IsNotOffered_OnceOthersInTheTeamWrote()
    {
        var view = RenderView(out _, SupportCaseVisibility.Team, mayBePrivate: false);

        Open(view, "Export is empty");

        Assert.False(view.FindComponent<SupportCaseVisibilityPicker>().Instance.MayBePrivate);
    }

    [Fact]
    public void AClosedCase_StillOffersWhoReadsIt()
    {
        var view = RenderView(out _, SupportCaseVisibility.Team, caseId: Case.Id, closed: true);

        Assert.Single(view.FindAll("[data-visibility]"));
    }

    private IRenderedComponent<SupportCasesView> RenderView() => RenderView(out _);

    private static void Open(IRenderedComponent<SupportCasesView> view, string subject)
        => view.FindAll("[data-case] [style*='cursor: pointer']").First(x => x.TextContent.Contains(subject)).Click();

    /// <summary>A case the reader has unread messages in is marked read as it opens, and not before.</summary>
    [Fact]
    public void OpeningACaseWithUnreadMessages_MarksItRead()
    {
        var view = RenderView(out var cases, unread: 1);

        cases.Verify(x => x.MarkReadAsync(TeamKey, Case.Id, default), Times.Never);
        Assert.Single(view.FindAll("[data-unread]"));

        Open(view, "Export is empty");

        cases.Verify(x => x.MarkReadAsync(TeamKey, Case.Id, default), Times.Once);
        Assert.Empty(view.FindAll("[data-unread]"));
    }

    [Fact]
    public void ADeepLink_OpensItsCase()
    {
        var view = RenderView(out _, caseId: Case.Id);

        Assert.Contains("We are looking into the export.", view.Markup);
    }

    private IRenderedComponent<SupportCasesView> RenderView(out Mock<ISupportCaseService> cases, SupportCaseVisibility? visibility = null, int unread = 0, string caseId = null, bool mayBePrivate = true, bool closed = false)
    {
        var shown = Case with { Visibility = visibility, Status = closed ? SupportCaseStatus.Closed : SupportCaseStatus.Open };

        cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 100, default))
            .ReturnsAsync(new SupportCasePage { Items = [shown] });
        cases.Setup(x => x.GetCaseAsync(TeamKey, Case.Id, default)).ReturnsAsync(shown);
        cases.Setup(x => x.GetVisibilityChoiceAsync(TeamKey, Case.Id, default)).ReturnsAsync(new SupportCaseVisibilityChoice(visibility != null, mayBePrivate));
        cases.Setup(x => x.GetMessagesAsync(TeamKey, Case.Id, null, SupportCaseLimits.MaxMessagesPerCase, default))
            .ReturnsAsync(new SupportMessagePage { Items = [Reply] });
        if (unread > 0)
            cases.Setup(x => x.GetMyUnreadByCaseAsync(TeamKey, default)).ReturnsAsync([new SupportCaseUnread { CaseId = Case.Id, Count = unread }]);

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
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        return Render<SupportCasesView>(p => p.Add(x => x.CaseId, caseId));
    }
}
