using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Mailbox;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The team's mail address, and the postmaster's view of mail that belongs to no team.
/// </summary>
public class MailboxViewRenderTests : BunitContext
{
    private const string TeamKey = "acme";
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TeamMailKey Primary = new() { Key = "8fjgthitwne3", Role = MailKeyRole.Primary, Address = "8fjgthitwne3-case@fortdocs.app", CreatedAt = Now };
    private static readonly TeamMailKey Retiring = new() { Key = "oldkey000000", Role = MailKeyRole.Secondary, Address = "oldkey000000-case@fortdocs.app", CreatedAt = Now.AddDays(-200), RetiresAt = Now.AddDays(30) };

    private static readonly PostmasterMailSummary Unrouted = new() { Id = "mail-1", From = "anna@example.org", Subject = "Begäran om handling", ReceivedAt = Now, Reason = MailRouteReason.RetiredKey };

    private static readonly InboundMailItem Opened = new()
    {
        Id = "mail-1",
        Envelope = new MailEnvelope { MessageId = "<m1@example.org>", From = "anna@example.org", FromName = "Anna", To = ["oldkey000000-case@fortdocs.app"], DeliveredTo = "oldkey000000-case@fortdocs.app", Subject = "Begäran om handling" },
        Reason = MailRouteReason.RetiredKey,
        ReceivedAt = Now
    };

    private void Common()
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.TryAddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));
    }

    private IRenderedComponent<TeamMailAddressView> RenderAddress(TeamMailAddresses addresses, out Mock<ITeamMailboxService> mailbox)
    {
        mailbox = new Mock<ITeamMailboxService>();
        mailbox.Setup(x => x.GetAddressesAsync(TeamKey, default)).ReturnsAsync(addresses);
        mailbox.Setup(x => x.EnsureKeyAsync(TeamKey, default)).ReturnsAsync(new TeamMailAddresses { TeamKey = TeamKey, Primary = Primary });
        mailbox.Setup(x => x.RotateKeyAsync(TeamKey, default)).ReturnsAsync(new TeamMailAddresses { TeamKey = TeamKey, Primary = Primary, Secondary = Retiring });

        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);

        Services.AddSingleton(mailbox.Object);
        Services.AddSingleton(teamState.Object);
        Common();

        return Render<TeamMailAddressView>();
    }

    private IRenderedComponent<PostmasterView> RenderPostmaster(out Mock<IPostmasterService> postmaster, bool permitted = true)
    {
        postmaster = new Mock<IPostmasterService>();
        if (permitted) postmaster.Setup(x => x.ListAsync(null, 50, default)).ReturnsAsync(new PostmasterPage { Items = [Unrouted] });
        else postmaster.Setup(x => x.ListAsync(null, 50, default)).ThrowsAsync(new UnauthorizedAccessException());
        postmaster.Setup(x => x.GetCountAsync(default)).ReturnsAsync(1);
        postmaster.Setup(x => x.OpenAsync("mail-1", default)).ReturnsAsync(Opened);
        postmaster.Setup(x => x.RouteAsync("mail-1", "acme", default)).ReturnsAsync(Opened with { TeamKey = "acme", Reason = MailRouteReason.Rerouted });

        Services.AddSingleton(postmaster.Object);
        Services.AddSingleton(Mock.Of<ITeamMailNotifier>());
        Common();

        return Render<PostmasterView>();
    }

    [Fact]
    public void TheTeamsAddress_IsShown_WithAWayToCopyIt()
    {
        var view = RenderAddress(new TeamMailAddresses { TeamKey = TeamKey, Primary = Primary }, out _);

        Assert.Contains("8fjgthitwne3-case@fortdocs.app", view.Markup);
        Assert.Contains("aria-label=\"Copy\"", view.Markup);
        Assert.DoesNotContain("Previous address", view.Markup);
    }

    [Fact]
    public void AnAddressBeingRetired_IsShownAsAWarning_WithWhenItStops()
    {
        var view = RenderAddress(new TeamMailAddresses { TeamKey = TeamKey, Primary = Primary, Secondary = Retiring }, out _);

        Assert.Contains("Previous address", view.Markup);
        Assert.Contains("oldkey000000-case@fortdocs.app", view.Markup);
        Assert.Contains("Still accepted until", view.Markup);
    }

    [Fact]
    public async Task ATeamWithNoAddress_CanCreateOne()
    {
        var view = RenderAddress(new TeamMailAddresses { TeamKey = TeamKey }, out var mailbox);

        Assert.Contains("The team has no mail address yet.", view.Markup);

        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Create address")).Click());

        mailbox.Verify(x => x.EnsureKeyAsync(TeamKey, default), Times.Once);
        Assert.Contains("8fjgthitwne3-case@fortdocs.app", view.Markup);
    }

    [Fact]
    public async Task ChangingTheAddress_AsksFirst_AndOnlyThenRotates()
    {
        var view = RenderAddress(new TeamMailAddresses { TeamKey = TeamKey, Primary = Primary }, out var mailbox);
        var dialogs = Render<RadzenDialog>();

        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Change address")).Click());

        mailbox.Verify(x => x.RotateKeyAsync(TeamKey, default), Times.Never);
        Assert.Contains("Everyone who writes to the team must be told the new address.", dialogs.Markup);

        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Last(b => b.TextContent.Contains("Change address")).Click());

        mailbox.Verify(x => x.RotateKeyAsync(TeamKey, default), Times.Once);
    }

    [Fact]
    public void WithoutTheGrant_TheAddressIsRefused_NotShown()
    {
        var mailbox = new Mock<ITeamMailboxService>();
        mailbox.Setup(x => x.GetAddressesAsync(TeamKey, default)).ThrowsAsync(new UnauthorizedAccessException());
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);
        Services.AddSingleton(mailbox.Object);
        Services.AddSingleton(teamState.Object);
        Common();

        var view = Render<TeamMailAddressView>();

        Assert.Contains("You do not have access to the team's mail address.", view.Markup);
    }

    [Fact]
    public void ThePostmaster_ListsWhoAboutWhatAndWhy_WithoutOpeningAnything()
    {
        var view = RenderPostmaster(out var postmaster);

        Assert.Contains("Begäran om handling", view.Markup);
        Assert.Contains("anna@example.org", view.Markup);
        Assert.Contains("Sent to a retired address", view.Markup);
        Assert.Contains("1 unrouted", view.Markup);
        Assert.Contains("Opening a mail is recorded", view.Markup);
        postmaster.Verify(x => x.OpenAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task OpeningAMail_ShowsItsEnvelope()
    {
        var view = RenderPostmaster(out var postmaster);

        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Begäran om handling")).Click());

        postmaster.Verify(x => x.OpenAsync("mail-1", default), Times.Once);
        Assert.Contains("Anna &lt;anna@example.org&gt;", view.Markup);
        Assert.Contains("Delivered to", view.Markup);
    }

    [Fact]
    public async Task AMailIsRoutedToTheTeamTheReaderNames()
    {
        var view = RenderPostmaster(out var postmaster);
        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Contains("Begäran om handling")).Click());

        await view.InvokeAsync(() => view.FindComponent<RadzenTextBox>().Instance.ValueChanged.InvokeAsync("acme"));
        await view.InvokeAsync(() => view.FindAll("button").First(b => b.TextContent.Trim() == "Route" || b.TextContent.Contains("Route") && !b.TextContent.Contains("team")).Click());

        postmaster.Verify(x => x.RouteAsync("mail-1", "acme", default), Times.Once);
    }

    [Fact]
    public void WithoutTheSystemGrant_ThePostmasterIsRefused_NotShown()
    {
        var view = RenderPostmaster(out _, permitted: false);

        Assert.Contains("You do not have access to mail that belongs to no team.", view.Markup);
        Assert.DoesNotContain("Begäran om handling", view.Markup);
    }
}
