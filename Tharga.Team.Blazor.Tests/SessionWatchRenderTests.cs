using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
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

    /// <summary>A context of its own, whose browser is <paramref name="browser"/>, so the test decides when the module loads and what it is.</summary>
    private static BunitContext Isolated(ControlledJsRuntime browser)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IJSRuntime>(browser);
        context.Services.AddSingleton<ISessionCircuit>(new FakeSessionCircuit());
        context.Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        context.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ThargaBlazorOptions { Sessions = SessionTestData.Options() }));
        context.Services.AddRadzenComponents();
        return context;
    }

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
    /// With the library's own sign-in, "Sign out" goes through <c>/logout?reason=signedout</c> with the session still running: the endpoint ends and announces
    /// it while the cookie still says who is signing out, which the identity provider's sign-out needs on a shared machine, and lands on the ended page. The
    /// circuit ends nothing first, and the other tabs are not told of an end that has not happened yet.
    /// </summary>
    [Fact]
    public void SignOut_WithTheLibrarysSignIn_GoesThroughLogoutWithTheReason_WithoutEndingTheSessionFirst()
    {
        Services.AddSingleton(new ThargaAuthOptions());
        var view = Watch();
        Warn(view);

        view.Find("[data-session-watch-signout]").Click();

        var navigation = Navigation;
        view.WaitForState(() => navigation.History.Count > 0);
        Assert.Equal(0, _circuit.SignOuts);
        Assert.Equal("http://localhost/logout?reason=signedout", navigation.Uri);
        Assert.True(navigation.History.First().Options.ForceLoad);
        Assert.Single(navigation.History);
        Assert.Equal(0, Calls("announce"));
        Assert.Equal(1, Calls("stopCountdown"));
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

    /// <summary>A host that signs people in by its own means has no <c>/logout</c> of the library's: the circuit ends the session, and the tab goes straight to the ended page.</summary>
    [Fact]
    public void SignOut_WithoutIt_EndsTheSessionInTheCircuit_AndGoesToTheEndedPath()
    {
        var view = Watch();
        Warn(view);

        view.Find("[data-session-watch-signout]").Click();

        var navigation = Navigation;
        view.WaitForState(() => navigation.History.Count > 0);
        Assert.Equal(1, _circuit.SignOuts);
        Assert.Equal("http://localhost/session-ended?reason=signedout", navigation.Uri);
        Assert.True(navigation.History.First().Options.ForceLoad);
        Assert.Equal("ended", LastCall("announce")[1]);
    }

    /// <summary>Without the library's sign-in, a sign-out the store could not take leaves the question open: nothing ended, so nothing navigates.</summary>
    [Fact]
    public void SignOut_WithoutIt_WhenTheCircuitCouldNotEndTheSession_StaysOnThePage()
    {
        _circuit.SignOutLeavesTheSession = true;
        var view = Watch();
        Warn(view);

        view.Find("[data-session-watch-signout]").Click();

        Assert.Equal(1, _circuit.SignOuts);
        Assert.Empty(Navigation.History);
        Assert.Single(view.FindAll("[data-session-watch-dialog]"));
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

    /// <summary>The server moved the idle moment while the question was open: the countdown starts over from what is left now, not from the old moment.</summary>
    [Fact]
    public void WhenTheIdleMomentMovesWhileAsking_TheCountdownStartsOver()
    {
        var view = Watch();
        Warn(view, seconds: 90);

        _circuit.Set(SessionCircuitState.Warning, TimeSpan.FromSeconds(150));
        view.WaitForState(() => Calls("startCountdown") == 2);

        Assert.Equal(150_000, Convert.ToInt32(LastCall("startCountdown")[1]));
        Assert.Contains("2:30", Text(view, "[data-session-watch-countdown]"));
        Assert.Single(view.FindAll("[data-session-watch-dialog]"));
    }

    [Fact]
    public void WhenTheWarningIsSaidAgainUnchanged_TheCountdownIsNotStartedAgain()
    {
        var view = Watch();
        Warn(view, seconds: 90);

        _circuit.Set(SessionCircuitState.Warning, TimeSpan.FromSeconds(90));

        Assert.Equal(1, Calls("startCountdown"));
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
        Assert.Equal(0, _circuit.ActivatedSubscribers);
        Assert.False(_circuit.Asked);
    }

    /// <summary>An import still pending when the view goes completes into nothing: no browser listeners, no reference back to a disposed view.</summary>
    [Fact]
    public async Task DisposedWhileTheModuleIsLoading_TheModuleIsReleased_AndNothingIsWatched()
    {
        var browser = new ControlledJsRuntime();
        using var context = Isolated(browser);
        var module = new RecordingModule();
        var circuit = (FakeSessionCircuit)context.Services.GetRequiredService<ISessionCircuit>();
        var view = context.Render<SessionWatch>();
        Assert.Equal(1, circuit.Attached);

        await context.DisposeComponentsAsync();
        browser.Import.SetResult(module);
        view.WaitForAssertion(() => Assert.True(module.Disposed));

        Assert.Empty(module.Calls);
        Assert.True(module.Disposed);
        Assert.Equal(0, circuit.Attached);
    }

    /// <summary>A change already being raised when the view went, and reaching it after, never attaches it again, loads the browser half or opens its dialog.</summary>
    [Fact]
    public async Task AChangeThatArrivesAfterDisposal_DoesNotAttachTheViewAgain()
    {
        var view = Watch();
        view.WaitForAssertion(() => Assert.Equal(1, Calls("watchActivity")));
        var inFlight = _circuit.ChangedListeners;

        await DisposeComponentsAsync();
        _circuit.Set(SessionCircuitState.Warning, TimeSpan.FromSeconds(90));
        await view.InvokeAsync(inFlight);
        await view.InvokeAsync(() => { });

        Assert.Equal(0, _circuit.Attached);
        Assert.False(_circuit.Asked);
        Assert.Equal(1, Calls("watchActivity"));
        Assert.Equal(0, Calls("startCountdown"));
        Assert.Equal(0, Calls("holdActivity"));
    }

    /// <summary>A browser half that times out while being released does not keep the view's place: another view can still take over.</summary>
    [Theory]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(ObjectDisposedException))]
    [InlineData(typeof(JSDisconnectedException))]
    public async Task WhenReleasingTheModuleFails_TheViewStillLetsGoOfTheCircuit(Type failure)
    {
        var browser = new ControlledJsRuntime();
        using var context = Isolated(browser);
        var module = new RecordingModule { DisposeFailure = (Exception)Activator.CreateInstance(failure, "the browser went away") };
        browser.Import.SetResult(module);
        var circuit = (FakeSessionCircuit)context.Services.GetRequiredService<ISessionCircuit>();
        var view = context.Render<SessionWatch>();
        view.WaitForAssertion(() => Assert.Contains("watchActivity", module.Calls));

        var exception = await Record.ExceptionAsync(() => context.DisposeComponentsAsync());

        Assert.Null(exception);
        Assert.Equal(0, circuit.Attached);
        Assert.False(circuit.Asked);
    }

    /// <summary>
    /// On a server circuit the first after-render arrives only when the browser acknowledges the render, so the warning can be shown before the browser half
    /// is loaded. Once it is, the countdown, the held activity and the focus are started for the question already open.
    /// </summary>
    [Fact]
    public async Task AWarningShownBeforeTheBrowserHalfLoaded_IsStartedOnceItLoads()
    {
        var view = Render<LateAfterRenderWatch>();
        Assert.Empty(_module.Invocations);

        await view.InvokeAsync(() => _circuit.Set(SessionCircuitState.Warning, TimeSpan.FromSeconds(90)));
        view.WaitForState(() => view.FindAll("[data-session-watch-dialog]").Count == 1);
        Assert.Equal(0, Calls("startCountdown"));

        await view.InvokeAsync(view.Instance.AcknowledgeFirstRenderAsync);

        Assert.Equal(1, Calls("watchActivity"));
        Assert.Equal(1, Calls("startCountdown"));
        Assert.Equal(90_000, Convert.ToInt32(LastCall("startCountdown")[1]));
        Assert.IsType<ElementReference>(LastCall("startCountdown")[2]);
        Assert.Equal(true, LastCall("holdActivity")[1]);
    }

    // ---- a session found after the view arrived (review M2)

    [Fact]
    public async Task ASessionFoundAfterTheViewArrived_IsWatchedThen()
    {
        _circuit.Active = false;
        var view = Watch();
        Assert.Equal(0, _circuit.Attached);
        Assert.Empty(JSInterop.Invocations);

        await view.InvokeAsync(_circuit.Activate);

        Assert.Equal(1, _circuit.Attached);
        Assert.Equal(1, Calls("watchActivity"));
    }

    /// <summary>The real circuit, whose sign-in state completes only after the view has rendered: the view still attaches and loads the browser half.</summary>
    [Fact]
    public async Task WithASignInStateReadLate_TheRealCircuitIsWatchedOnceItIsRead()
    {
        using var context = new BunitContext();
        var clock = new SessionClock();
        var store = new InMemorySessionStore();
        store.Put(SessionTestData.Session("s1", clock.Now));
        var cache = new SessionCache(clock);
        var options = SessionTestData.Options();
        var policy = new TestSessionPolicy { Default = new SessionLimits { IdleTimeout = TimeSpan.FromMinutes(10) } };
        var evaluator = new SessionEvaluator(cache, SessionTestData.Wrap(options), clock, policy, SessionTestData.Sessions(store, clock, notifier: cache));
        var auth = new ServerAuthenticationStateProvider();
        var state = new TaskCompletionSource<AuthenticationState>();
        auth.SetAuthenticationState(state.Task);
        var circuit = new SessionCircuit(evaluator, auth, auth, cache, SessionTestData.Wrap(options), clock);
        context.Services.AddSingleton<ISessionCircuit>(circuit);
        context.Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        context.Services.AddSingleton(SessionTestData.Wrap(options));
        context.Services.AddRadzenComponents();
        var module = context.JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Loose;

        var view = context.Render<SessionWatch>();
        var start = circuit.StartAsync();
        Assert.Empty(module.Invocations);

        state.SetResult(new AuthenticationState(SessionTestData.WithSession("s1")));
        await start;
        await circuit.SettledAsync();

        view.WaitForAssertion(() => Assert.Single(module.Invocations, x => x.Identifier == "watchActivity"));
        circuit.Dispose();
    }

    // ---- one dialog per circuit, its own elements, and what a screen reader hears (review M7)

    [Fact]
    public void ASecondWatchOnTheCircuit_LoadsNothing_AndShowsNoSecondDialog()
    {
        var first = Watch();
        var second = Render<SessionWatch>();

        Warn(first);

        Assert.Equal(1, _circuit.Attached);
        Assert.Equal(1, Calls("watchActivity"));
        Assert.Empty(second.FindAll("[data-session-watch-dialog]"));
    }

    [Fact]
    public void TheCountdown_IsGivenTheDialogsOwnElement_NotASelectorForThePage()
    {
        var view = Watch();

        Warn(view);

        Assert.IsType<ElementReference>(LastCall("startCountdown")[2]);
    }

    [Fact]
    public void TheTickingCountdown_IsHiddenFromScreenReaders_AndTheDeadlineIsReadOnce()
    {
        var view = Watch();

        Warn(view);

        var countdown = view.Find("[data-session-watch-countdown]");
        Assert.Equal("true", countdown.GetAttribute("aria-hidden"));
        Assert.Null(countdown.GetAttribute("aria-live"));

        var described = view.Find("[data-session-watch-dialog]").GetAttribute("aria-describedby").Split(' ');
        Assert.Equal(2, described.Length);
        Assert.Equal(SessionText.Body.Default, view.Find($"[id='{described[0]}']").TextContent.Trim());
        Assert.Contains("1:30", view.Find($"[id='{described[1]}']").TextContent);
    }
}
