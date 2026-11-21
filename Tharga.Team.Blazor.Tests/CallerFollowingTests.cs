using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A component that decides what to show from the caller's claims follows them when they change (Tharga/Team#505, BL0013). The toolkit changes a circuit's
/// claims while it runs, so a view that read them once keeps offering what the caller has lost until the page is reloaded.
/// </summary>
/// <remarks>
/// <b>The refusals are the tests to keep.</b> The case that matters is the loss: a caller whose access is taken away must stop seeing what it gave them
/// without a reload. Gaining access follows from the same code, and is asserted too.
/// </remarks>
public class CallerFollowingTests : BunitContext
{
    private static ClaimsPrincipal Caller(params string[] systemScopes)
        => new(new ClaimsIdentity(systemScopes.Select(x => new Claim(TeamClaimTypes.SystemScope, x)), "Test"));

    /// <summary>An auth-state provider the test can change, raising the event the framework raises.</summary>
    private sealed class ChangingProvider(ClaimsPrincipal first) : AuthenticationStateProvider
    {
        private ClaimsPrincipal _current = first;

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_current));

        public void Change(ClaimsPrincipal next)
        {
            _current = next;
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(next)));
        }
    }

    // --- the follower ---

    [Fact]
    public async Task TheFollower_ReturnsTheCaller_ThenTellsOfEveryChange()
    {
        var provider = new ChangingProvider(Caller("a"));
        var told = new List<ClaimsPrincipal>();
        using var follower = new CallerFollower(provider, user => { told.Add(user); return Task.CompletedTask; });

        var first = await follower.StartAsync();
        var next = Caller("b");
        provider.Change(next);

        Assert.True(first.HasClaim(TeamClaimTypes.SystemScope, "a"));
        Assert.Same(next, Assert.Single(told));
    }

    /// <summary>Listening first means a change between the two is delivered rather than lost, and the start itself is not reported as a change.</summary>
    [Fact]
    public async Task TheStart_IsNotReportedAsAChange()
    {
        var provider = new ChangingProvider(Caller("a"));
        var told = 0;
        using var follower = new CallerFollower(provider, _ => { told++; return Task.CompletedTask; });

        await follower.StartAsync();

        Assert.Equal(0, told);
    }

    [Fact]
    public async Task ADisposedFollower_NoLongerTellsAnything()
    {
        var provider = new ChangingProvider(Caller("a"));
        var told = 0;
        var follower = new CallerFollower(provider, _ => { told++; return Task.CompletedTask; });
        await follower.StartAsync();

        follower.Dispose();
        provider.Change(Caller("b"));

        Assert.Equal(0, told);
    }

    /// <summary>It runs from an event nobody awaits, so an exception that escaped would end the circuit.</summary>
    [Fact]
    public async Task AFailingCallback_NeverEscapesTheEvent()
    {
        var provider = new ChangingProvider(Caller("a"));
        var told = 0;
        using var follower = new CallerFollower(provider, _ => { told++; throw new InvalidOperationException("boom"); });
        await follower.StartAsync();

        provider.Change(Caller("b"));
        provider.Change(Caller("c"));

        Assert.Equal(2, told);
    }

    // --- the users list ---

    private ChangingProvider RenderedUsers(ClaimsPrincipal first, out IRenderedComponent<UsersListView<ITeamMember>> view)
    {
        var users = new IUser[] { new TestUser("u1", "id-1", "anna@example.com", "Anna Andersson") };
        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetAsync()).Returns(users.ToAsyncEnumerable());
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(users[0]);
        var teams = new Mock<ITeamDirectoryService>();
        teams.Setup(x => x.GetTeamsAsync<ITeamMember>()).Returns(AsyncEnumerable.Empty<ITeam<ITeamMember>>());
        var oversight = new Mock<ITeamOversightService>();
        oversight.Setup(x => x.GetAllTeamsAsync<ITeamMember>()).Returns(AsyncEnumerable.Empty<ITeam<ITeamMember>>());
        var directory = new Mock<IUserDirectoryService>();
        directory.SetupGet(x => x.IsConfigured).Returns(false);

        var provider = new ChangingProvider(first);
        Services.AddSingleton(userService.Object);
        Services.AddSingleton(teams.Object);
        Services.AddSingleton(oversight.Object);
        Services.AddSingleton(Mock.Of<IUserManagementService>());
        Services.AddSingleton<AuthenticationStateProvider>(provider);
        Services.AddSingleton(directory.Object);
        Services.AddSingleton(new IconSettings());
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        view = Render<UsersListView<ITeamMember>>();
        return provider;
    }

    private sealed record TestUser(string Key, string Identity, string EMail, string Name) : IUser;

    [Fact]
    public void TheUsersList_IsDropped_WhenTheCallerLosesUsersManage()
    {
        var provider = RenderedUsers(Caller(SystemUserScopes.Manage), out var view);
        view.WaitForAssertion(() => Assert.Contains("Anna Andersson", view.Markup));

        provider.Change(Caller());

        view.WaitForAssertion(() => Assert.DoesNotContain("Anna Andersson", view.Markup));
        Assert.Contains(SystemUserScopes.Manage, view.Markup);
    }

    [Fact]
    public void TheUsersList_AppearsWithoutAReload_WhenTheCallerIsGivenUsersManage()
    {
        var provider = RenderedUsers(Caller(), out var view);
        Assert.DoesNotContain("Anna Andersson", view.Markup);

        provider.Change(Caller(SystemUserScopes.Manage));

        view.WaitForAssertion(() => Assert.Contains("Anna Andersson", view.Markup));
    }

    /// <summary>A circuit that ends must not leave a handler on a provider that outlives the component.</summary>
    [Fact]
    public async Task ADisposedUsersList_NoLongerListens()
    {
        var provider = RenderedUsers(Caller(SystemUserScopes.Manage), out var view);
        view.WaitForAssertion(() => Assert.Contains("Anna Andersson", view.Markup));
        var before = view.RenderCount;

        await DisposeComponentsAsync();
        provider.Change(Caller());

        Assert.Equal(before, view.RenderCount);
    }
}
