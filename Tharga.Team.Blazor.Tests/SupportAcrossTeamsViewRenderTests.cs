using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Support staff working every team's cases from one queue.
/// </summary>
public class SupportAcrossTeamsViewRenderTests : BunitContext
{
    private static readonly SupportCase FromAcme = new()
    {
        Id = "case-acme",
        TeamKey = "acme",
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    private static readonly SupportCase FromGlobex = new()
    {
        Id = "case-globex",
        TeamKey = "globex",
        AuthorIdentity = "bob",
        AuthorName = "Bob",
        Subject = "Cannot invite",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc),
        MessageCount = 1
    };

    private static readonly SupportMessage Asked = new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Body = "The export file has no rows.",
        SentAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void ItListsCasesFromEveryTeam_NamingEachTeam()
    {
        var view = RenderView(out _);

        Assert.Contains("Export is empty", view.Markup);
        Assert.Contains("Cannot invite", view.Markup);
        Assert.Contains("Acme", view.Markup);
        Assert.Contains("globex", view.Markup);
    }

    [Fact]
    public void ItShowsHowManyAreAwaitingAnAnswer()
    {
        var view = RenderView(out _);

        Assert.Contains("2 awaiting an answer", view.Markup);
    }

    [Fact]
    public void OpeningACaseShowsItsTranscript()
    {
        var view = RenderView(out _);

        Assert.DoesNotContain("The export file has no rows.", view.Markup);

        Subject(view, "Export is empty").Click();

        Assert.Contains("The export file has no rows.", view.Markup);
    }

    /// <summary>
    /// Every action carries the case's own team, so the service checks it exactly as in that team's queue.
    /// </summary>
    [Fact]
    public void AnsweringACase_RepliesInThatCasesTeam()
    {
        var view = RenderView(out var cases);

        Subject(view, "Export is empty").Click();
        view.Find("textarea").Input("We are looking at it.");
        view.FindAll("button").First(b => b.TextContent.Trim() == "Answer").Click();

        cases.Verify(x => x.ReplyToCaseAsync("acme", "case-acme", "We are looking at it.", default), Times.Once);
    }

    /// <summary>A change made on another instance reaches no in-process notifier, so the host asks for the re-read.</summary>
    [Fact]
    public async Task RefreshingReReadsTheQueue()
    {
        var view = RenderView(out var cases);

        cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 20, default))
            .ReturnsAsync(new SupportCasePage { Items = [FromAcme, FromGlobex, FromAcme with { Id = "case-new", Subject = "Arrived elsewhere" }] });

        await view.Instance.RefreshAsync();

        Assert.Contains("Arrived elsewhere", view.Markup);
    }

    [Fact]
    public void WithoutTheCrossTeamGrant_ItRendersANoteRatherThanThrowing()
    {
        var cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 20, default))
            .ThrowsAsync(new UnauthorizedAccessException("no grant"));

        var view = Render(cases);

        Assert.Contains("do not have access", view.Markup);
    }

    private static AngleSharp.Dom.IElement Subject(IRenderedComponent<SupportAcrossTeamsView> view, string subject)
        => view.FindAll("button").First(b => b.TextContent.Contains(subject));

    private IRenderedComponent<SupportAcrossTeamsView> RenderView(out Mock<ISupportCaseService> cases)
    {
        cases = new Mock<ISupportCaseService>();
        cases.Setup(x => x.GetCasesAcrossTeamsAsync(null, 20, default))
            .ReturnsAsync(new SupportCasePage { Items = [FromAcme, FromGlobex] });
        cases.Setup(x => x.GetAwaitingSupportCountAcrossTeamsAsync(default)).ReturnsAsync(2);
        cases.Setup(x => x.GetCaseAsync("acme", FromAcme.Id, default)).ReturnsAsync(FromAcme);
        cases.Setup(x => x.GetMessagesAsync("acme", FromAcme.Id, null, 50, default))
            .ReturnsAsync(new SupportMessagePage { Items = [Asked] });

        return Render(cases);
    }

    private IRenderedComponent<SupportAcrossTeamsView> Render(Mock<ISupportCaseService> cases)
    {
        Services.AddSingleton(cases.Object);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Oversight());
        Services.AddRadzenComponents();

        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportAcrossTeamsView>();
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
