using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Resolving the selected team records the member's activity in it, not only selecting one (Tharga/Team#317).
/// A member who never switched teams kept the Last Seen of the day they joined.
/// </summary>
/// <remarks>
/// Drives <see cref="TeamStateService"/> itself, like <c>SelectedTeamNotificationTests</c>: the defect was
/// which path stamped, so a helper test could not have caught it. Throttling is the team service's job and is
/// covered there.
/// </remarks>
public class SelectedTeamActivityStampTests
{
    private const string TeamKey = "team-1";

    private readonly Mock<ITeamService> _teamService = new();
    private readonly Mock<ILocalStorageService> _localStorage = new();
    private readonly Mock<IJSRuntime> _js = new();
    private readonly Mock<AuthenticationStateProvider> _authenticationStateProvider = new();
    private readonly FakeNavigationManager _navigation = new();

    public SelectedTeamActivityStampTests()
    {
        _localStorage
            .Setup(x => x.GetItemAsStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string)null);

        _js
            .Setup(x => x.InvokeAsync<IJSVoidResult>(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns(new ValueTask<IJSVoidResult>(Mock.Of<IJSVoidResult>()));
    }

    private TeamStateService CreateSut()
        => new(_teamService.Object, _navigation, _localStorage.Object, _js.Object, _authenticationStateProvider.Object, Options.Create(new ThargaBlazorOptions()));

    private void HasTeams(params ITeam[] teams)
        => _teamService.Setup(x => x.GetTeamsAsync()).Returns(() => AsAsync(teams));

    /// <summary>Signed in with the claims already naming <paramref name="currentTeamKey"/>, so the team is restored, not chosen.</summary>
    private void SignedIn(string currentTeamKey = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "tester") };
        if (currentTeamKey != null) claims.Add(new Claim(Constants.TeamKeyCookie, currentTeamKey));

        _authenticationStateProvider
            .Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))));
    }

    private static async IAsyncEnumerable<ITeam> AsAsync(ITeam[] teams)
    {
        await Task.CompletedTask;
        foreach (var team in teams) yield return team;
    }

    /// <summary>The bug's shape: a returning member whose one team is restored from the claim, with no selection.</summary>
    [Fact]
    public async Task RestoringTheTeam_RecordsActivityInIt()
    {
        HasTeams(new FakeTeam(TeamKey, "Alpha"));
        SignedIn(currentTeamKey: TeamKey);

        var team = await CreateSut().GetSelectedTeamAsync();

        Assert.Equal(TeamKey, team?.Key);
        _teamService.Verify(x => x.SetMemberLastSeenAsync(TeamKey), Times.Once);
    }

    /// <summary>Every resolve records activity, including when the selection is unchanged; the team service throttles the writes.</summary>
    [Fact]
    public async Task AnUnchangedSelection_StillRecordsActivity()
    {
        HasTeams(new FakeTeam(TeamKey, "Alpha"));
        SignedIn(currentTeamKey: TeamKey);
        var sut = CreateSut();

        await sut.GetSelectedTeamAsync();
        await sut.GetSelectedTeamAsync();

        _teamService.Verify(x => x.SetMemberLastSeenAsync(TeamKey), Times.Exactly(2));
    }

    [Fact]
    public async Task WithNoTeam_NothingIsRecorded()
    {
        HasTeams();
        SignedIn();

        var team = await CreateSut().GetSelectedTeamAsync();

        Assert.Null(team);
        _teamService.Verify(x => x.SetMemberLastSeenAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>Activity tracking must never break a page: a store fault while stamping still returns the team.</summary>
    [Fact]
    public async Task AFailedStamp_StillReturnsTheTeam()
    {
        HasTeams(new FakeTeam(TeamKey, "Alpha"));
        SignedIn(currentTeamKey: TeamKey);
        _teamService.Setup(x => x.SetMemberLastSeenAsync(TeamKey)).ThrowsAsync(new TimeoutException("store down"));

        var team = await CreateSut().GetSelectedTeamAsync();

        Assert.Equal(TeamKey, team?.Key);
    }

    [Fact]
    public async Task ASignedOutCaller_RecordsNothing()
    {
        _authenticationStateProvider
            .Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        await CreateSut().GetSelectedTeamAsync();

        _teamService.Verify(x => x.SetMemberLastSeenAsync(It.IsAny<string>()), Times.Never);
    }

    private sealed record FakeTeam(string Key, string Name) : ITeam
    {
        public string Icon => null;
    }

    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }

        protected override void NavigateToCore(string uri, NavigationOptions options) { }
    }
}
