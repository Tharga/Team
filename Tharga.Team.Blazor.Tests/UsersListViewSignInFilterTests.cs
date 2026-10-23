using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Filtering the users list by how users sign in: the choices offered before and after everyone's methods are read,
/// reading them in batches with progress and a way to stop, and the filtered list itself.
/// </summary>
public class UsersListViewSignInFilterTests : BunitContext
{
    private static readonly DirectoryUserIdentity Broker = new("federated", "https://broker.example/", "sub");
    private static readonly DirectoryUserIdentity Mail = new("emailAddress", "example.com", "anna@example.com");

    private sealed record TestUser(string Key, string Identity, string EMail, string Name, string DirectoryId) : IUser;

    private readonly Mock<IUserManagementService> _management = new();
    private readonly List<string[]> _calls = [];

    private IRenderedComponent<UsersListView<ITeamMember>> RenderList(int linked, int unlinked = 2, bool withIdentityDirectory = true, Func<string[], CancellationToken, Task<UserSignInIdentities[]>> answer = null)
    {
        answer ??= (keys, _) => Task.FromResult(IdentitiesOf(keys));

        _management
            .Setup(x => x.GetSignInIdentitiesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<string> keys, CancellationToken token) =>
            {
                var asked = keys.ToArray();
                lock (_calls) _calls.Add(asked);
                return answer(asked, token);
            });

        var users = Enumerable.Range(1, linked).Select(x => new TestUser($"user-{x:000}", $"id-{x}", $"user{x}@example.com", $"User {x:000}", $"dir-{x:000}"))
            .Concat(Enumerable.Range(1, unlinked).Select(x => new TestUser($"unlinked-{x}", $"uid-{x}", $"unlinked{x}@example.com", $"Unlinked {x}", null)))
            .Cast<IUser>()
            .ToArray();

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

        var directory = new Mock<IUserDirectoryService>();
        directory.SetupGet(x => x.IsConfigured).Returns(true);

        Services.AddSingleton(userService.Object);
        Services.AddSingleton(teams.Object);
        Services.AddSingleton(oversight.Object);
        Services.AddSingleton(_management.Object);
        Services.AddSingleton(auth.Object);
        Services.AddSingleton(directory.Object);
        if (withIdentityDirectory) Services.AddSingleton(Mock.Of<IUserIdentityDirectory>());
        Services.AddSingleton(new SignInMethodOptions().Add(x => x.Issuer == "https://broker.example/" ? new SignInMethod("e-ID") : null));
        Services.AddSingleton(new IconSettings());
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<UsersListView<ITeamMember>>();
        view.WaitForAssertion(() => Assert.NotNull(view.Find("table")));

        return view;
    }

    /// <summary>Every user signs in with an email address; the even-numbered also with the broker, which the host names "e-ID".</summary>
    private static UserSignInIdentities[] IdentitiesOf(string[] keys)
        => [.. keys.Select(x => new UserSignInIdentities(x, int.Parse(x[^3..]) % 2 == 0 ? [Broker, Mail] : [Mail]))];

    private static RadzenDropDown<SignInFilter> Filter(IRenderedComponent<UsersListView<ITeamMember>> view)
        => view.FindComponent<RadzenDropDown<SignInFilter>>().Instance;

    private static string[] Choices(IRenderedComponent<UsersListView<ITeamMember>> view)
        => [.. view.FindComponent<RadzenDropDown<SignInFilter>>().FindAll(".rz-dropdown-item").Select(x => x.TextContent.Trim())];

    private static string[] ReadAllButton(IRenderedComponent<UsersListView<ITeamMember>> view)
        => [.. view.FindAll("[data-signin-read-all]").Select(x => x.TextContent.Trim())];

    private static void ReadAll(IRenderedComponent<UsersListView<ITeamMember>> view)
    {
        view.Find("[data-signin-read-all]").Click();
        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-signin-read-all]")), TimeSpan.FromSeconds(10));
    }

    /// <summary>The names in the rows the grid is showing, in order. A single page shows no pager, so the paging summary cannot be read.</summary>
    private static string[] ShownNames(IRenderedComponent<UsersListView<ITeamMember>> view)
        => [.. view.FindAll("tr.rz-data-row").Select(x => x.QuerySelectorAll("td")[1].TextContent.Trim())];

    private int KeysRead
    {
        get { lock (_calls) return _calls.SelectMany(x => x).Distinct().Count(); }
    }

    [Fact]
    public void BeforeEveryoneIsRead_OnlyNotLinkedIsOffered_AndReadingEveryoneIsOn_Offer()
    {
        var view = RenderList(linked: 25);

        Assert.Equal(["Not linked"], Choices(view));
        Assert.Single(ReadAllButton(view));
    }

    [Fact]
    public void ReadingEveryone_ReadsEachUserOnce_InBatches_AndOffersEveryMethodThatExists()
    {
        var view = RenderList(linked: 25);

        ReadAll(view);

        Assert.Equal(25, KeysRead);
        Assert.Equal(_calls.SelectMany(x => x).Count(), KeysRead);
        Assert.Equal(
            ["Not linked", "Signs in with e-ID", "Does not sign in with e-ID", "Signs in with Email", "Does not sign in with Email"],
            Choices(view));
    }

    [Fact]
    public async Task AFilterOnALackedMethod_ShowsOnlyTheUsersWhoWereReadAndLackIt()
    {
        var view = RenderList(linked: 25);
        ReadAll(view);

        await view.InvokeAsync(() => Filter(view).ValueChanged.InvokeAsync(SignInFilter.Lacks("e-ID")));

        view.WaitForAssertion(() => Assert.Contains("13 users", view.Markup));
        Assert.NotEmpty(view.FindAll("[data-signin-note]"));
    }

    [Fact]
    public async Task AFilterOnAHeldMethod_ShowsOnlyTheUsersWhoSignInWithIt()
    {
        var view = RenderList(linked: 25);
        ReadAll(view);

        await view.InvokeAsync(() => Filter(view).ValueChanged.InvokeAsync(SignInFilter.Has("e-ID")));

        view.WaitForAssertion(() => Assert.Contains("12 users", view.Markup));
    }

    [Fact]
    public async Task NotLinked_FindsTheUsersWithNoDirectoryLink_WithoutReadingAnyone()
    {
        var view = RenderList(linked: 25, unlinked: 2);
        var before = KeysRead;

        await view.InvokeAsync(() => Filter(view).ValueChanged.InvokeAsync(SignInFilter.NotLinked));

        view.WaitForAssertion(() => Assert.Equal(["Unlinked 1", "Unlinked 2"], ShownNames(view)));
        Assert.Equal(before, KeysRead);
        Assert.Empty(view.FindAll("[data-signin-note]"));
    }

    [Fact]
    public async Task ClearingTheFilter_ShowsEveryoneAgain()
    {
        var view = RenderList(linked: 25, unlinked: 2);
        await view.InvokeAsync(() => Filter(view).ValueChanged.InvokeAsync(SignInFilter.NotLinked));
        view.WaitForAssertion(() => Assert.Equal(["Unlinked 1", "Unlinked 2"], ShownNames(view)));

        await view.InvokeAsync(() => Filter(view).ValueChanged.InvokeAsync(null));

        view.WaitForAssertion(() => Assert.Contains("27 users", view.Markup));
    }

    [Fact]
    public async Task WhileReading_ProgressIsShown_AndStoppingKeepsWhatWasRead()
    {
        var second = new TaskCompletionSource<UserSignInIdentities[]>();
        var calls = 0;
        var view = RenderList(linked: 130, answer: (keys, _) => ++calls == 3 ? second.Task : Task.FromResult(IdentitiesOf(keys)));

        view.Find("[data-signin-read-all]").Click();
        view.WaitForAssertion(() => Assert.Contains("Reading sign-in methods: 50 of 122", view.Markup), TimeSpan.FromSeconds(10));
        Assert.NotEmpty(view.FindAll("[data-signin-progress]"));

        view.Find("[data-signin-cancel]").Click();
        second.SetResult(IdentitiesOf(["user-051"]));

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-signin-progress]")), TimeSpan.FromSeconds(10));
        Assert.Single(ReadAllButton(view));
        Assert.Equal(["Not linked"], Choices(view));
        Assert.True(KeysRead < 130);
    }

    [Fact]
    public void AFailureWhileReading_IsReported_AndAskingAgainContinuesWhereItStopped()
    {
        var failing = true;
        var calls = 0;
        var view = RenderList(linked: 130, answer: (keys, _) => ++calls == 3 && failing
            ? throw new TimeoutException("The directory is down.")
            : Task.FromResult(IdentitiesOf(keys)));

        view.Find("[data-signin-read-all]").Click();
        view.WaitForAssertion(() => Assert.Contains(Services.GetRequiredService<NotificationService>().Messages, x => x.Summary == "Could not read how everyone signs in"), TimeSpan.FromSeconds(10));
        Assert.Single(ReadAllButton(view));

        failing = false;
        view.Find("[data-signin-read-all]").Click();
        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-signin-read-all]")), TimeSpan.FromSeconds(10));
        Assert.Equal(130, KeysRead);
    }

    [Fact]
    public void WithoutADirectoryThatCanReadIdentities_NoFilterIsOffered()
    {
        var view = RenderList(linked: 25, withIdentityDirectory: false);

        Assert.Empty(view.FindAll("[data-signin-filter]"));
        Assert.Empty(view.FindAll("[data-signin-read-all]"));
    }
}
