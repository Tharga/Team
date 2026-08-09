using System.Text.RegularExpressions;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Tracks which components still render literal user-facing text instead of resolving it through
/// <see cref="IThargaTextProvider"/> (Tharga/Team#204).
/// </summary>
/// <remarks>
/// <b>A ratchet, not a pass/fail gate.</b> Migrating every component is a large sweep; blocking the build
/// until it finished would mean landing it as one unreviewable change. Instead each component is either
/// **migrated** — zero literals, and it stays that way — or **pending** with a recorded count that may only
/// go down. A new literal in a migrated component fails immediately; a sweep that regresses a pending
/// component fails too.
/// <para>
/// <b>The counts below are the remaining work for #204</b>, and they are meant to reach zero. When one does,
/// move that file into <see cref="Migrated"/> so it can never slip back.
/// </para>
/// <para>
/// This scans **attribute** strings only — `Text=`, `Title=`, `Placeholder=`, `title=`. Inline prose between
/// tags is user-facing too and is <b>not</b> covered here, because distinguishing it from markup, bound
/// expressions and scope names like <c>team:manage</c> needs judgement a regex does not have. So a zero here
/// means "no literal attribute text", not "fully translated" — stated plainly so the number is not read as
/// more than it is.
/// </para>
/// </remarks>
public class TextCoverageTests
{
    /// <summary>Components fully migrated: these must stay at zero literal attribute strings.</summary>
    private static readonly string[] Migrated =
    [
        "Features/Team/TeamSelector.razor",
        "Features/Authentication/LoginDisplay.razor",
    ];

    /// <summary>Not yet migrated, with the count as it stands. These may only go down.</summary>
    private static readonly Dictionary<string, int> Pending = new()
    {
        ["Features/Team/TeamComponent.razor"] = 24,
        ["Features/User/UsersView.razor"] = 3,
        ["Features/Audit/AuditLogView.razor"] = 47,
    };

    // A literal display string: an attribute whose value starts with a capital and contains no binding.
    private static readonly Regex LiteralText =
        new(@"(?:Text|Title|Placeholder|title)=""([A-Z][^""{@]{2,})""", RegexOptions.Compiled);

    private static string ComponentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor");
    }

    private static int CountLiterals(string relativePath)
    {
        var full = Path.Combine(ComponentRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"'{relativePath}' was not found — the scan is pointed at the wrong place, or the file moved.");

        return LiteralText.Matches(File.ReadAllText(full)).Count;
    }

    [Theory]
    [MemberData(nameof(MigratedComponents))]
    public void AMigratedComponent_RendersNoLiteralText(string relativePath)
    {
        var count = CountLiterals(relativePath);

        Assert.True(count == 0,
            $"'{relativePath}' is recorded as migrated but has {count} literal attribute string(s). " +
            "Add a TextKey to the component's catalogue and resolve it through the TextSet, or a consumer " +
            "overriding the toolkit's wording will find this one still in English.");
    }

    [Theory]
    [MemberData(nameof(PendingComponents))]
    public void APendingComponent_DoesNotGrow(string relativePath, int allowed)
    {
        var count = CountLiterals(relativePath);

        Assert.True(count <= allowed,
            $"'{relativePath}' has {count} literal attribute string(s), up from the recorded {allowed}. " +
            "#204 is a ratchet — this number may only go down. Route the new string through the text catalogue.");

        Assert.True(count == allowed || count < allowed);
        if (count < allowed)
        {
            // Not a failure, but the record is now wrong and the next reader would trust it.
            Assert.Fail(
                $"'{relativePath}' is down to {count} literal string(s) from {allowed} — good, but update the " +
                "recorded count (or move it to Migrated if it is zero) so the remaining work stays accurate.");
        }
    }

    /// <summary>
    /// The scan can silently match nothing — a moved file, a broken regex — and every assertion above would
    /// pass while checking nothing. This proves it still finds what it is supposed to find.
    /// </summary>
    [Fact]
    public void TheScan_ActuallyFindsLiterals()
    {
        Assert.NotEmpty(Migrated);
        Assert.NotEmpty(Pending);

        // The largest pending component is the proof the regex still matches real markup.
        Assert.True(CountLiterals("Features/Audit/AuditLogView.razor") > 0,
            "The scan found no literal text in AuditLogView, which is not credible — the regex or the path is broken.");

        Assert.Matches(LiteralText, """<RadzenButton Text="Save" />""");
        Assert.DoesNotMatch(LiteralText, """<RadzenButton Text="@_saveText" />""");
    }

    /// <summary>Every key the toolkit exposes must be discoverable, or a consumer cannot translate it.</summary>
    [Fact]
    public void EveryCatalogueKey_IsDiscoverable()
    {
        var all = ThargaTextKeys.All;

        Assert.NotEmpty(all);
        Assert.Contains(all, k => k.Key == TeamMenuText.Team.Key);
        Assert.Contains(all, k => k.Key == TeamSelectorText.Suspended.Key);
        Assert.Contains(all, k => k.Key == AccessLevelText.Owner.Key);
        Assert.All(all, k => Assert.False(string.IsNullOrWhiteSpace(k.Default)));
    }

    public static TheoryData<string> MigratedComponents()
    {
        var data = new TheoryData<string>();
        foreach (var path in Migrated) data.Add(path);
        return data;
    }

    public static TheoryData<string, int> PendingComponents()
    {
        var data = new TheoryData<string, int>();
        foreach (var (path, count) in Pending) data.Add(path, count);
        return data;
    }
}
