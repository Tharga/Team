namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The one browser signal (Tharga/Team#399): a module that watches for use of a visible tab and tells the circuit at most once an interval, and the two places
/// that load it. These read the files, as the session script's tests do; what the circuit does with the report is <c>ActivityTrackerTests</c>.
/// </summary>
public class ActivityScriptTests
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
    public void TheModule_ExportsAWatcherAndAStop()
    {
        var script = Read("wwwroot/activity.js");

        Assert.Contains("export function watchActivity", script);
        Assert.Contains("export function stopWatching", script);
    }

    [Theory]
    [InlineData("pointerdown")]
    [InlineData("keydown")]
    [InlineData("wheel")]
    public void TheModule_WatchesForUse(string eventName)
    {
        Assert.Contains($"'{eventName}'", Read("wwwroot/activity.js"));
    }

    [Fact]
    public void TheModule_ReportsOnlyFromAVisibleTab_AndOnlyOncePerInterval()
    {
        var script = Read("wwwroot/activity.js");

        Assert.Contains("visibilityState", script);
        Assert.Contains("'visible'", script);
        Assert.Contains("intervalMs", script);
    }

    /// <summary>The first use after the page opens is reported at once, so a short visit still leaves a trace.</summary>
    [Fact]
    public void TheFirstUse_IsReportedAtOnce()
    {
        Assert.Contains("let last = 0;", Read("wwwroot/activity.js"));
    }

    [Fact]
    public void TheModule_KeepsNothingInTheBrowser()
    {
        var script = Read("wwwroot/activity.js");

        Assert.DoesNotContain("localStorage", script);
        Assert.DoesNotContain("sessionStorage", script);
    }

    /// <summary>The support desk used to watch on its own, and only on the page that hosts it. It follows the signal now, so the old module is gone.</summary>
    [Fact]
    public void TheSupportDesk_HasNoWatcherOfItsOwn()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Tharga.Team.Blazor"))) root = root.Parent;
        Assert.NotNull(root);

        Assert.False(File.Exists(Path.Combine(root.FullName, "Tharga.Team.Blazor", "wwwroot", "support-desk.js")));
        Assert.DoesNotContain("support-desk.js", Read("Features/Support/SupportDeskStatus.razor"));
    }

    [Theory]
    [InlineData("Features/Authentication/LoginDisplay.razor")]
    [InlineData("Features/Support/SupportDeskStatus.razor")]
    public void TheWatcher_IsIncludedWhereSomeoneIsAtWork(string razor)
    {
        Assert.Contains("<ActivityWatcher />", Read(razor));
    }
}
