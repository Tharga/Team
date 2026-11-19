using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Resolving the selected team while the page is being statically rendered: there is no JavaScript to read the remembered team from yet, which is
/// not a fault and must not be reported as one (Tharga/Team#516).
/// </summary>
/// <remarks>
/// <b>The shape it took in production</b>: with prerendering on, every first GET of an interactive page raised the sign-in state change, the handler
/// resolved the selection again, and reading local storage threw. The interactive render that followed resolved the team normally, so nothing broke
/// for the reader, but one application wrote 310 exceptions in a week, the largest group in its telemetry, which buried real faults.
/// </remarks>
public class TeamStateServicePrerenderTests
{
    private const string TeamKey = "team-1";

    private const string PrerenderMessage =
        "JavaScript interop calls cannot be issued at this time. This is because the component is being statically rendered. When prerendering is enabled, JavaScript interop calls can only be performed during the OnAfterRenderAsync lifecycle method.";

    private readonly Mock<ITeamService> _teamService = new();
    private readonly Mock<ILocalStorageService> _local = new();
    private readonly Mock<ITeamEntryService> _entry = new();
    private readonly List<string> _scripts = [];
    private readonly FakeNavigationManager _navigation = new();
    private readonly RecordingLogger _logger = new();
    private readonly ChangingAuthState _auth;

    public TeamStateServicePrerenderTests()
    {
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        team.SetupGet(x => x.Name).Returns("Acme");
        _teamService.Setup(x => x.GetTeamPageAsync(It.IsAny<TeamPageRequest>())).ReturnsAsync(() => new TeamPage([TeamSummary.Of(team.Object)], null));

        _entry.Setup(x => x.CheckAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TeamEntryDecision.Allow);

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester"), new Claim(Constants.TeamKeyCookie, TeamKey)], "Test"));
        _auth = new ChangingAuthState(principal);
    }

    private TeamStateService CreateSut(bool withEntry = true)
    {
        var js = new Mock<IJSRuntime>();
        js.Setup(x => x.InvokeAsync<IJSVoidResult>("eval", It.IsAny<object[]>()))
            .Callback<string, object[]>((_, args) => _scripts.Add((string)args[0]))
            .Returns(ValueTask.FromResult<IJSVoidResult>(null));

        return new TeamStateService(_teamService.Object, _navigation, _local.Object, js.Object, _auth, Options.Create(new ThargaBlazorOptions()), _logger, withEntry ? _entry.Object : null);
    }

    private void StorageIsUnavailableDuringPrerender()
        => _local.Setup(x => x.GetItemAsStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException(PrerenderMessage));

    private void StorageIsAvailable()
        => _local.Setup(x => x.GetItemAsStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string)null);

    private async Task WaitForTheHandlerAsync()
    {
        SpinWait.SpinUntil(() => _local.Invocations.Count > 0, TimeSpan.FromSeconds(2));
        await Task.Delay(100, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolvingTheSelection_WhileStaticallyRendered_DoesNotThrow_AndLeavesItUnresolved()
    {
        StorageIsUnavailableDuringPrerender();
        var sut = CreateSut();

        var team = await sut.GetSelectedTeamAsync();

        Assert.Null(team);
        Assert.False(sut.TryGetSelectedTeam(out _));
    }

    /// <summary>Nothing that needs JavaScript is attempted either: no cookie is written and the page is not reloaded for a selection that was never made.</summary>
    [Fact]
    public async Task WhileStaticallyRendered_NoCookieIsSet_AndNoReloadHappens()
    {
        StorageIsUnavailableDuringPrerender();
        var sut = CreateSut();

        await sut.GetSelectedTeamAsync();

        Assert.Empty(_scripts);
        Assert.Equal(0, _navigation.RefreshCount);
    }

    [Fact]
    public async Task WhileStaticallyRendered_NoSelectionChangeIsAnnounced()
    {
        StorageIsUnavailableDuringPrerender();
        var sut = CreateSut();
        var raised = 0;
        sut.SelectedTeamChangedEvent += (_, _) => raised++;

        await sut.GetSelectedTeamAsync();

        Assert.Equal(0, raised);
    }

    /// <summary>The production shape: the sign-in state changes during the prerender, and the handler resolves the selection again.</summary>
    [Fact]
    public async Task TheSignInStateChanging_WhileStaticallyRendered_LogsNothing()
    {
        StorageIsUnavailableDuringPrerender();
        _ = CreateSut();

        _auth.Change();
        await WaitForTheHandlerAsync();

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task OnceInteractive_TheSelectionResolvesNormally()
    {
        StorageIsUnavailableDuringPrerender();
        var sut = CreateSut();
        await sut.GetSelectedTeamAsync();

        StorageIsAvailable();
        var team = await sut.GetSelectedTeamAsync();

        Assert.Equal(TeamKey, team?.Key);
        Assert.True(sut.TryGetSelectedTeam(out var selected));
        Assert.Equal(TeamKey, selected.Key);
    }

    /// <summary>Only the prerender case is let through: any other failure reading storage is still a fault, and still reported.</summary>
    [Fact]
    public async Task AnotherFailureReadingStorage_IsNotSwallowed()
    {
        _local.Setup(x => x.GetItemAsStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("The storage quota was exceeded."));
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetSelectedTeamAsync());
    }

    [Fact]
    public async Task AnotherFailureReadingStorage_StillLogsAWarning_FromTheSignInHandler()
    {
        _local.Setup(x => x.GetItemAsStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("The storage quota was exceeded."));
        _ = CreateSut();

        _auth.Change();
        await WaitForTheHandlerAsync();

        Assert.Contains(_logger.Entries, x => x.Level == LogLevel.Warning);
    }

    private sealed class FakeNavigationManager : NavigationManager
    {
        public int RefreshCount { get; private set; }

        public FakeNavigationManager() => Initialize("https://localhost/", "https://localhost/");

        protected override void NavigateToCore(string uri, NavigationOptions options) => RefreshCount++;
    }

    private sealed class ChangingAuthState(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));

        public void Change() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private sealed class RecordingLogger : ILogger<TeamStateService>
    {
        public List<(LogLevel Level, Exception Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, exception));
    }
}
