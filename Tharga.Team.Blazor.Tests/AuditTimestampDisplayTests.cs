using System.Text.RegularExpressions;
using Tharga.Team.Blazor.Features.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every timestamp in <see cref="AuditLogView"/> shows the time itself, not how long ago it was.
/// </summary>
/// <remarks>
/// A relative label is worked out when its row renders and never again, so rows rendered moments apart
/// disagreed about "now": two entries written in the same millisecond read five seconds apart. The exact
/// order of near-simultaneous entries is what an audit log is read for, and an absolute timestamp cannot
/// go stale. No bUnit render of this view exists, so the choice is pinned in the markup, the way
/// <c>ScopeVisibilityWiringTests</c> pins its wiring.
/// </remarks>
public class AuditTimestampDisplayTests
{
    private const string Absolute = "Display=\"EDateTimeDisplay.Absolute\"";
    private static readonly Regex DateTimeViews = new(@"<DateTimeView\b[^>]*>");

    [Fact]
    public void EveryTimestamp_InTheAuditLog_IsAbsolute()
    {
        var views = DateTimeViews.Matches(File.ReadAllText(ComponentPath("Features/Audit/AuditLogView.razor")))
            .Select(x => x.Value)
            .ToArray();

        Assert.NotEmpty(views);
        Assert.All(views, x => Assert.Contains(Absolute, x));
    }

    private static string ComponentPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor", relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
