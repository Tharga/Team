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
/// The team's mail address. The postmaster's view has tests of its own, in <see cref="PostmasterViewRenderTests"/>.
/// </summary>
public class MailboxViewRenderTests : BunitContext
{
    private const string TeamKey = "acme";
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TeamMailKey Primary = new() { Key = "8fjgthitwne3", Role = MailKeyRole.Primary, Address = "8fjgthitwne3-case@fortdocs.app", CreatedAt = Now };
    private static readonly TeamMailKey Retiring = new() { Key = "oldkey000000", Role = MailKeyRole.Secondary, Address = "oldkey000000-case@fortdocs.app", CreatedAt = Now.AddDays(-200), RetiresAt = Now.AddDays(30) };

    private void Common()
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.TryAddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));
    }

    private IRenderedComponent<TeamMailAddressView> RenderAddress(TeamMailAddresses addresses, out Mock<ITeamMailboxService> mailbox, bool allowChange = true)
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

        return Render<TeamMailAddressView>(p => p.Add(x => x.AllowChange, allowChange));
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
    public void AReaderWhoMayNotChangeTheAddress_SeesItWithoutTheButtons()
    {
        var view = RenderAddress(new TeamMailAddresses { TeamKey = TeamKey, Primary = Primary }, out _, allowChange: false);

        Assert.Contains("8fjgthitwne3-case@fortdocs.app", view.Markup);
        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent.Contains("Change address"));
    }

    [Fact]
    public void AReaderWhoMayNotCreateTheAddress_IsNotOfferedTo()
    {
        var view = RenderAddress(new TeamMailAddresses { TeamKey = TeamKey }, out _, allowChange: false);

        Assert.DoesNotContain(view.FindAll("button"), b => b.TextContent.Contains("Create address"));
    }
}
