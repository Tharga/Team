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

        view.FindAll("button").First(b => b.TextContent.Contains("Export is empty")).Click();

        Assert.Contains("We are looking into the export.", view.Markup);
    }

    /// <summary>Clicking the open case again puts the transcript away.</summary>
    [Fact]
    public void ClickingItAgainClosesTheConversation()
    {
        var view = RenderView();

        var subject = () => view.FindAll("button").First(b => b.TextContent.Contains("Export is empty"));

        subject().Click();
        Assert.Contains("We are looking into the export.", view.Markup);

        subject().Click();
        Assert.DoesNotContain("We are looking into the export.", view.Markup);
    }

    private IRenderedComponent<SupportCasesView> RenderView()
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetMyCasesAsync(TeamKey, null, 20, default))
            .ReturnsAsync(new SupportCasePage { Items = [Case] });
        cases.Setup(x => x.GetCaseAsync(TeamKey, Case.Id, default)).ReturnsAsync(Case);
        cases.Setup(x => x.GetMessagesAsync(TeamKey, Case.Id, null, 50, default))
            .ReturnsAsync(new SupportMessagePage { Items = [Reply] });

        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);

        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);

        Services.AddSingleton(cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();

        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportCasesView>();
    }
}
