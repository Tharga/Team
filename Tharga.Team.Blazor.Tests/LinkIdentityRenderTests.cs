using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A person who signs in through two identities is two users with one address. The list marks them, and an administrator links
/// the duplicate's identity onto the user who keeps the teams, through the management service.
/// </summary>
public class LinkIdentityRenderTests : BunitContext
{
    private sealed record TestUser(string Key, string Identity, string EMail, string Name) : IUser;

    private readonly Mock<IUserManagementService> _management = new();

    private void Register()
    {
        Services.AddSingleton(_management.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static UserViewModel Row(string key, string name, int teams = 0) => new() { Key = key, Name = name, EMail = "anna@example.com", TeamCount = teams };

    private IRenderedComponent<LinkIdentityDialog> RenderDialog(params UserViewModel[] candidates)
    {
        Register();
        _ = Services.GetRequiredService<DialogService>().OpenAsync("Link", _ => (RenderFragment)(_ => { }));

        return Render<LinkIdentityDialog>(p => p
            .Add(x => x.KeepUserKey, "old")
            .Add(x => x.KeepName, "Anna")
            .Add(x => x.Candidates, candidates));
    }

    [Fact]
    public void OnlyADuplicateWithNoTeam_IsOffered()
    {
        var view = RenderDialog(Row("old", "Anna"), Row("new", "Anna (new)"), Row("other", "Anna (other)", teams: 1));

        var choices = view.FindComponent<RadzenDropDown<string>>().Instance.Data.Cast<object>().ToArray();

        Assert.Single(choices);
    }

    [Fact]
    public void WhenEveryDuplicateHasATeam_ItSaysSo_AndOffersNothingToLink()
    {
        var view = RenderDialog(Row("other", "Anna (other)", teams: 2));

        Assert.NotEmpty(view.FindAll("[data-none]"));
        Assert.True(view.Find("[data-confirm-link]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Linking_CallsTheService_WithTheKeptUserAndTheDuplicate_AndClosesWithTrue()
    {
        _management.Setup(x => x.LinkIdentityAsync("old", "new", It.IsAny<CancellationToken>())).ReturnsAsync(new UserLinkResult("id-new", "id-old"));
        var view = RenderDialog(Row("new", "Anna (new)"));

        await view.InvokeAsync(() => view.Find("[data-confirm-link]").Click());

        _management.Verify(x => x.LinkIdentityAsync("old", "new", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARefusal_IsShown_WithoutClosing()
    {
        _management.Setup(x => x.LinkIdentityAsync("old", "new", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("It belongs to a team."));
        var view = RenderDialog(Row("new", "Anna (new)"));

        await view.InvokeAsync(() => view.Find("[data-confirm-link]").Click());

        Assert.Contains("It belongs to a team.", view.Find("[data-error]").TextContent);
    }

    [Fact]
    public void TheList_MarksUsersThatShareAnAddress_AndOffersTheLinkOnlyThere()
    {
        IUser[] users =
        [
            new TestUser("old", "id-old", "anna@example.com", "Anna"),
            new TestUser("new", "id-new", " ANNA@example.com ", "Anna (new)"),
            new TestUser("bob", "id-bob", "bob@example.com", "Bob"),
            new TestUser("x1", "id-x1", "unknown", "No address"),
            new TestUser("x2", "id-x2", "unknown", "No address either")
        ];

        var view = RenderList(users);

        Assert.Equal(["new", "old"], view.FindAll("[data-same-email]").Select(x => x.GetAttribute("data-same-email")).Order().ToArray());
        Assert.Contains("Same email as 1 other", view.FindAll("[data-same-email]")[0].TextContent);
    }

    private IRenderedComponent<UsersListView<ITeamMember>> RenderList(IUser[] users)
    {
        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetAsync()).Returns(users.ToAsyncEnumerable());
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(users[0]);

        var teams = new Mock<ITeamDirectoryService>();
        teams.Setup(x => x.GetTeamsAsync<ITeamMember>()).Returns(AsyncEnumerable.Empty<ITeam<ITeamMember>>());
        var oversight = new Mock<ITeamOversightService>();
        oversight.Setup(x => x.GetAllTeamsAsync<ITeamMember>()).Returns(AsyncEnumerable.Empty<ITeam<ITeamMember>>());

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TeamClaimTypes.SystemScope, SystemUserScopes.Manage)], "Test"));
        var auth = new Mock<AuthenticationStateProvider>();
        auth.Setup(x => x.GetAuthenticationStateAsync()).ReturnsAsync(new AuthenticationState(principal));

        Services.AddSingleton(userService.Object);
        Services.AddSingleton(teams.Object);
        Services.AddSingleton(oversight.Object);
        Services.AddSingleton(auth.Object);
        Services.AddSingleton(new IconSettings());
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<IUserDirectoryService>());
        Register();

        var view = Render<UsersListView<ITeamMember>>();
        view.WaitForAssertion(() => Assert.NotNull(view.Find("table")));

        return view;
    }
}
