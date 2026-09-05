using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
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
    private static readonly SupportCase Case = new()
    {
        Id = "case-1",
        TeamKey = null,
        AuthorIdentity = null,
        AuthorName = "stranger@example.com",
        Subject = "Cannot sign in",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    private static readonly SupportMessage Arrived = new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorIdentity = null,
        AuthorName = "stranger@example.com",
        Body = "It says my key expired.",
        SentAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc),
        Source = SupportChannelType.Email
    };

    [Fact]
    public void TheQueueRendersWhatArrivedWithNoTeam()
    {
        var view = RenderView(out _);

        Assert.Contains("Cannot sign in", view.Markup);
        Assert.Contains("stranger@example.com", view.Markup);
    }

    [Fact]
    public void TheTranscriptIsHiddenUntilTheCaseIsOpened()
    {
        var view = RenderView(out _);

        Assert.DoesNotContain("It says my key expired.", view.Markup);
    }

    [Fact]
    public void ClickingTheCaseShowsItsTranscript()
    {
        var view = RenderView(out _);

        Subject(view).Click();

        Assert.Contains("It says my key expired.", view.Markup);

        // Provenance, which is the reason an agent looks at an unassigned case differently: it was
        // attributed on a From header rather than by an authenticated caller.
        Assert.Contains("Email", view.Markup);
    }

    /// <summary>
    /// The whole point of the component: an operator who knows whose case it is gives it a team.
    /// </summary>
    [Fact]
    public async Task ChoosingATeamAndAssigning_AssignsTheCase()
    {
        var view = RenderView(out var cases);

        Subject(view).Click();

        var dropdown = view.FindComponent<RadzenDropDown<string>>();
        await view.InvokeAsync(() => dropdown.Instance.ValueChanged.InvokeAsync("acme"));

        view.FindAll("button").First(b => b.TextContent.Trim() == "Assign").Click();

        cases.Verify(x => x.AssignCaseAsync("case-1", "acme", default), Times.Once);
    }

    /// <summary>
    /// Losing the race must say so. A queue that silently swallows the second click is how an operator stops
    /// trusting it.
    /// </summary>
    [Fact]
    public async Task AssigningACaseSomebodyElseTook_SaysSo()
    {
        var view = RenderView(out var cases, assignSucceeds: false);

        Subject(view).Click();

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

        Subject(view).Click();

        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent.Trim() == "Assign");
        Assert.Contains("cross-team read grant", view.Markup);
    }

    /// <summary>
    /// A refused read renders a note rather than taking the page down: this is a panel beside content the
    /// operator can see.
    /// </summary>
    [Fact]
    public void WithoutTheSystemGrant_ItRendersANoteRatherThanThrowing()
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetUnassignedCasesAsync(null, 20, default))
            .ThrowsAsync(new UnauthorizedAccessException("no grant"));

        var view = Render(cases, withOversight: true);

        Assert.Contains("do not have access", view.Markup);
        Assert.DoesNotContain("Cannot sign in", view.Markup);
    }

    private static AngleSharp.Dom.IElement Subject(IRenderedComponent<SupportUnassignedView> view)
        => view.FindAll("button").First(b => b.TextContent.Contains("Cannot sign in"));

    private IRenderedComponent<SupportUnassignedView> RenderView(
        out Mock<ISupportCaseService> cases,
        bool withOversight = true,
        bool assignSucceeds = true)
    {
        cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetUnassignedCasesAsync(null, 20, default))
            .ReturnsAsync(new SupportCasePage { Items = [Case] });
        cases.Setup(x => x.GetCaseAsync(null, Case.Id, default)).ReturnsAsync(Case);
        cases.Setup(x => x.GetMessagesAsync(null, Case.Id, null, 50, default))
            .ReturnsAsync(new SupportMessagePage { Items = [Arrived] });
        cases.Setup(x => x.AssignCaseAsync(Case.Id, It.IsAny<string>(), default)).ReturnsAsync(assignSucceeds);

        return Render(cases, withOversight);
    }

    private IRenderedComponent<SupportUnassignedView> Render(Mock<ISupportCaseService> cases, bool withOversight)
    {
        Services.AddSingleton(cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();

        if (withOversight) Services.AddSingleton(Oversight());

        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportUnassignedView>();
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
