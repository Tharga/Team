using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The "Still here?" dialog (Tharga/Team#471): a view over the circuit's state and nothing more. The server decides everything; the dialog shows a countdown it
/// was given, asks, and passes the answer on. The browser half is the <c>session-activity.js</c> module, set up here as bUnit's module interop.
/// </summary>
public class SessionWatchRenderTests : BunitContext
{
    private const string ModulePath = "./_content/Tharga.Team.Blazor/session-activity.js";

    private readonly FakeSessionCircuit _circuit = new();
    private readonly BunitJSModuleInterop _module;

    public SessionWatchRenderTests()
    {
        Services.AddSingleton<ISessionCircuit>(_circuit);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ThargaBlazorOptions { Sessions = SessionTestData.Options() }));
        Services.AddRadzenComponents();

        _module = JSInterop.SetupModule(ModulePath);
        _module.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<SessionWatch> Watch() => Render<SessionWatch>();

    private static string Text(IRenderedComponent<SessionWatch> view, string selector) => view.Find(selector).TextContent.Trim();

    private int Calls(string identifier) => _module.Invocations.Where(x => x.Identifier == identifier).Count();

    private object[] LastCall(string identifier) => _module.Invocations.Last(x => x.Identifier == identifier).Arguments.ToArray();

    private BunitNavigationManager Navigation => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    /// <summary>The circuit warns, as the real one does from another thread; waits for the view to have shown it.</summary>
    private void Warn(IRenderedComponent<SessionWatch> view, int seconds = 90)
    {
        _circuit.Set(SessionCircuitState.Warning, TimeSpan.FromSeconds(seconds));
        view.WaitForState(() => view.FindAll("[data-session-watch-dialog]").Count == 1);
    }

    private static void WaitForClosed(IRenderedComponent<SessionWatch> view) => view.WaitForState(() => view.FindAll("[data-session-watch-dialog]").Count == 0);

    // ---- nothing to show

    [Fact]
    public void WithNoCircuitRegistered_NothingIsRenderedOrLoaded()
    {
        using var services = new BunitContext();
        services.Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        services.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ThargaBlazorOptions()));
        services.Services.AddRadzenComponents();
        services.JSInterop.Mode = JSRuntimeMode.Strict;

        var view = services.Render<SessionWatch>();

        Assert.Empty(view.Markup.Trim());
    }

    [Fact]
    public void WhenSessionsAreNotActive_NothingIsRenderedOrLoaded()
    {
        _circuit.Active = false;

        var view = Watch();

        Assert.Empty(view.Markup.Trim());
        Assert.Equal(0, _circuit.Attached);
        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public void WhileTheSessionLasts_NoDialogIsShown_ButTheModuleIsLoadedAndTheViewAttached()
    {
        var view = Watch();

        Assert.Empty(view.FindAll("[data-session-watch-dialog]"));
        Assert.Equal(1, _circuit.Attached);
        Assert.Equal(1, _circuit.Subscribers);
        Assert.Equal(1, Calls("watchActivity"));
    }

    [Fact]
    public void TheActivityWatch_IsGivenTheActivityInterval_AndTheEndpoint()
    {
        Watch();

        var call = LastCall("watchActivity");
        Assert.Equal((int)TimeSpan.FromMinutes(1).TotalMilliseconds, Convert.ToInt32(call[1]));
        Assert.EndsWith("/_tharga/session/activity", Convert.ToString(call[2]));
    }

    // ---- the question

    [Fact]
    public void WhenTheCircuitWarns_TheDialogAsksInTheLibrarysWords()
    {
        var view = Watch();

        Warn(view);

        Assert.Equal(SessionText.Title.Default, Text(view, "[data-session-watch-title]"));
        Assert.Equal(SessionText.Body.Default, Text(view, "[data-session-watch-body]"));
        Assert.Equal(SessionText.Stay.Default, Text(view, "[data-session-watch-stay]"));
        Assert.Equal(SessionText.SignOut.Default, Text(view, "[data-session-watch-signout]"));
    }

    [Fact]
    public void TheDialog_IsAnAccessibleModalAlert()
    {
        var view = Watch();

        Warn(view);

        var dialog = view.Find("[data-session-watch-dialog]");
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.False(string.IsNullOrWhiteSpace(dialog.GetAttribute("aria-labelledby")));
        Assert.NotNull(view.Find("[data-session-watch-overlay]"));
    }

    [Fact]
    public void TheCountdown_StartsFromWhatTheServerSaysIsLeft_AndOnlyThen()
    {
        var view = Watch();
        Assert.Equal(0, Calls("startCountdown"));

        Warn(view);

        Assert.Equal(1, Calls("startCountdown"));
        Assert.Equal(90_000, Convert.ToInt32(LastCall("startCountdown")[1]));
        Assert.Contains("1:30", Text(view, "[data-session-watch-countdown]"));
    }

    /// <summary>The page holds back its own activity reports while the question is open, or answering by moving the mouse would count and the server would never be asked.</summary>
    [Fact]
    public void WhileTheQuestionIsOpen_ActivityIsHeld()
    {
        var view = Watch();

        Warn(view);

        Assert.Equal(true, LastCall("holdActivity")[1]);
    }

    // ---- the answers

    [Fact]
    public void StayingSignedIn_TellsTheCircuit_TellsOtherTabs_AndClosesTheDialog()
    {
        var view = Watch();
        Warn(view);

        view.Find("[data-session-watch-stay]").Click();
        WaitForClosed(view);

        Assert.Equal(1, _circuit.Stays);
        Assert.Equal("activity", LastCall("announce")[1]);
        Assert.Equal(false, LastCall("holdActivity")[1]);
        Assert.Equal(1, Calls("stopCountdown"));
    }

    [Fact]
    public void SigningOut_TellsTheCircuit_AndGoesWhereItSays_WithAFullLoad()
    {
        var view = Watch();
        Warn(view);

        view.Find("[data-session-watch-signout]").Click();

        var navigation = Navigation;
        view.WaitForState(() => navigation.History.Count > 0);
        Assert.Equal(1, _circuit.SignOuts);
        Assert.Equal("http://localhost/session-ended?reason=signedout", navigation.Uri);
        Assert.True(navigation.History.First().Options.ForceLoad);
    }

    /// <summary>
    /// With the library's own sign-in, "Sign out" goes through <c>/logout?reason=signedout</c>, so the identity provider is signed out too on a shared machine,
    /// and the browser still lands on the ended page. The session is already ended by the circuit, so the endpoint writes nothing more.
    /// </summary>
    [Fact]
    public void SignOut_WithTheLibrarysSignIn_GoesThroughLogoutWithTheReason()
    {
        Services.AddSingleton(new ThargaAuthOptions());
        var view = Watch();
        Warn(view);

        view.Find("[data-session-watch-signout]").Click();

        var navigation = Navigation;
        view.WaitForState(() => navigation.History.Count > 0);
        Assert.Equal(1, _circuit.SignOuts);
        Assert.Equal("http://localhost/logout?reason=signedout", navigation.Uri);
        Assert.True(navigation.History.First().Options.ForceLoad);
        Assert.Single(navigation.History);
    }

    [Fact]
    public void SignOut_WithTheLibrarysSignIn_FollowsItsLogoutPath()
    {
        Services.AddSingleton(new ThargaAuthOptions { LogoutPath = "/account/sign-out" });
        var view = Watch();
        Warn(view);

        view.Find("[data-session-watch-signout]").Click();

        var navigation = Navigation;
        view.WaitForState(() => navigation.History.Count > 0);
        Assert.Equal("http://localhost/account/sign-out?reason=signedout", navigation.Uri);
    }

    /// <summary>A host that signs people in by its own means has no <c>/logout</c> of the library's: the tab goes straight to the ended page.</summary>
    [Fact]
    public void SignOut_WithoutIt_GoesToTheEndedPath()
    {
        var view = Watch();
        Warn(view);

        view.Find("[data-session-watch-signout]").Click();

        var navigation = Navigation;
        view.WaitForState(() => navigation.History.Count > 0);
        Assert.Equal("http://localhost/session-ended?reason=signedout", navigation.Uri);
        Assert.True(navigation.History.First().Options.ForceLoad);
    }

    /// <summary>Only the person's own choice goes through the sign-out: a session that ends any other way lands on the ended page, sign-in or not.</summary>
    [Fact]
    public void WhenTheSessionEndsOtherwise_WithTheLibrarysSignIn_TheTabStillGoesToTheEndedLocation()
    {
        Services.AddSingleton(new ThargaAuthOptions());
        var view = Watch();

        _circuit.Set(SessionCircuitState.Ended, endedLocation: "/session-ended?reason=idle");

        var navigation = Navigation;
        view.WaitForState(() => navigation.History.Count > 0);
        Assert.Equal("http://localhost/session-ended?reason=idle", navigation.Uri);
    }

    [Fact]
    public void WhenTheSessionEnds_TheTabGoesToTheEndedLocation_AndTellsOtherTabs()
    {
        var view = Watch();

        _circuit.Set(SessionCircuitState.Ended, endedLocation: "/session-ended?reason=idle");

        var navigation = Navigation;
        view.WaitForState(() => navigation.History.Count > 0);
        Assert.Equal("http://localhost/session-ended?reason=idle", navigation.Uri);
        Assert.True(navigation.History.First().Options.ForceLoad);
        Assert.Equal(1, Calls("announce"));
        Assert.Equal("ended", LastCall("announce")[1]);
        Assert.Empty(view.FindAll("[data-session-watch-dialog]"));
    }

    [Fact]
    public void WhenTheWarningClearsElsewhere_TheDialogCloses()
    {
        var view = Watch();
        Warn(view);

        _circuit.Set(SessionCircuitState.Watching);
        WaitForClosed(view);

        Assert.Empty(view.FindAll("[data-session-watch-dialog]"));
        Assert.Equal(1, Calls("stopCountdown"));
        Assert.Equal(0, _circuit.Stays);
    }

    // ---- other tabs

    [Theory]
    [InlineData("activity")]
    [InlineData("ended")]
    public async Task APeersSignal_MakesTheCircuitLookAtTheStore(string kind)
    {
        var view = Watch();

        await view.InvokeAsync(() => view.Instance.OnPeerSignal(kind));

        Assert.Equal(1, _circuit.Checks);
    }

    [Fact]
    public async Task ASignalOfAnyOtherKind_IsIgnored()
    {
        var view = Watch();

        await view.InvokeAsync(() => view.Instance.OnPeerSignal("whatever-a-page-posts"));
        await view.InvokeAsync(() => view.Instance.OnPeerSignal(null));

        Assert.Equal(0, _circuit.Checks);
    }

    // ---- going away

    [Fact]
    public async Task WhenDisposed_TheModuleStopsWatching_TheViewDetaches_AndTheCircuitIsNotListenedToAnyMore()
    {
        var view = Watch();
        Warn(view);

        await DisposeComponentsAsync();

        Assert.Equal(1, Calls("stopWatching"));
        Assert.True(Calls("stopCountdown") >= 1);
        Assert.Equal(0, _circuit.Attached);
        Assert.Equal(0, _circuit.Subscribers);
    }
}
