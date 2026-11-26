namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The browser half of idle time (Tharga/Team#471): a small module that, like the support desk's presence reporter, watches for use of a visible tab and tells the
/// server at most once per interval, coordinates the "Still here?" warning between tabs, and the watch component that loads it only when sessions are active. These read the files, as the panel style tests do; what the
/// server does with the report is <c>SessionActivityEndpointTests</c>.
/// </summary>
public class SessionActivityScriptTests
{
    private static string Read(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor"))) dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir.FullName, "Tharga.Team.Blazor", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relativePath} does not exist.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheModule_Exists_AndIsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(Read("wwwroot/session-activity.js")));
    }

    [Fact]
    public void TheModule_ExportsWhatItsPrecedentDoes_AWatcherAndAStop()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("export function watchActivity", script);
        Assert.Contains("export function stopWatching", script);
    }

    /// <summary>The set the support desk watches: pointer, key and wheel, so a person reading or typing counts and a tab left open does not.</summary>
    [Theory]
    [InlineData("pointerdown")]
    [InlineData("keydown")]
    [InlineData("wheel")]
    public void TheModule_WatchesForUse(string eventName)
    {
        Assert.Contains($"'{eventName}'", Read("wwwroot/session-activity.js"));
    }

    [Fact]
    public void TheModule_ReportsOnlyFromAVisibleTab_AndOnlyOncePerInterval()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("visibilityState", script);
        Assert.Contains("'visible'", script);
        Assert.Contains("intervalMs", script);
    }

    [Fact]
    public void TheModule_PostsToTheServersEndpoint_WithTheCookie_AndKeepsNothingInTheBrowser()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("/_tharga/session/activity", script);
        Assert.Contains("POST", script);
        Assert.Contains("same-origin", script);
        Assert.DoesNotContain("localStorage", script);
        Assert.DoesNotContain("sessionStorage", script);
    }

    [Fact]
    public void TheModule_ExportsTheCoordinationTheWarningNeeds()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("export function holdActivity", script);
        Assert.Contains("export function announce", script);
        Assert.Contains("export function startCountdown", script);
        Assert.Contains("export function stopCountdown", script);
    }

    /// <summary>Tabs of one browser tell each other through one channel, and a message says only what kind of thing happened.</summary>
    [Fact]
    public void TheModule_TalksToOtherTabsOverOneChannel_WithMessagesThatCarryOnlyAKind()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("BroadcastChannel", script);
        Assert.Contains("'tharga-session'", script);
        Assert.Contains("'activity'", script);
        Assert.Contains("'ended'", script);
        Assert.Matches(@"postMessage\(\s*\{\s*kind\s*\}\s*\)", script);
        Assert.Contains("OnPeerSignal", script);
    }

    /// <summary>The countdown is only a display: a timeout chain on the monotonic clock, running only while the dialog is open. Nothing polls.</summary>
    [Fact]
    public void TheCountdown_IsATimeoutChainOnTheMonotonicClock_AndNothingPolls()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Contains("performance.now()", script);
        Assert.Contains("setTimeout", script);
        Assert.DoesNotContain("setInterval", script);
    }

    [Fact]
    public void TheModule_MakesOneKindOfServerCall_TheActivityReport()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.Equal(1, script.Split("fetch(").Length - 1);
    }

    [Fact]
    public void TheModule_KeepsNothingInTheBrowser_EvenForTheCoordination()
    {
        var script = Read("wwwroot/session-activity.js");

        Assert.DoesNotContain("localStorage", script);
        Assert.DoesNotContain("sessionStorage", script);
        Assert.DoesNotContain("document.cookie", script);
    }

    /// <summary>The sign-in menu no longer loads the module: the watch component does, so a host without the menu can place it alone.</summary>
    [Fact]
    public void TheSignInMenu_NoLongerLoadsTheModule_ButPlacesTheWatchAfterItsAuthorizeView()
    {
        var razor = Read("Features/Authentication/LoginDisplay.razor");

        Assert.DoesNotContain("session-activity.js", razor);
        Assert.DoesNotContain("watchActivity", razor);
        Assert.Contains("<SessionWatch />", razor);
        Assert.True(razor.IndexOf("<SessionWatch />", StringComparison.Ordinal) > razor.IndexOf("</AuthorizeView>", StringComparison.Ordinal), "The watch belongs after the AuthorizeView, so it is there for both states.");
    }

    [Fact]
    public void TheWatch_LoadsTheModule_ForSessionsThatAreActive_AndIsToldByPeers()
    {
        var razor = Read("Features/Authentication/SessionWatch.razor");

        Assert.Contains("session-activity.js", razor);
        // Only when sessions are active: the reporter is a call a minute that does nothing for a host with no policy.
        Assert.Contains(".Active", razor);
        Assert.Contains("[JSInvokable]", razor);
        Assert.Contains("OnPeerSignal", razor);
        Assert.Contains("alertdialog", razor);
        Assert.Contains("aria-modal", razor);
    }
}
