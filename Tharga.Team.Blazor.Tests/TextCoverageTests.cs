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
/// <b>What is scanned.</b> All three categories of user-facing string — markup attributes, prose between
/// tags, and literals in the C# block — across a component's <c>.razor</c> <b>and its sibling
/// <c>.razor.cs</c></b>. A zero therefore means "no literal display text found by the scan", not "provably
/// fully translated"; the scan is a stable, directional heuristic rather than a parser.
/// </para>
/// <para>
/// <b>Components are discovered, not listed.</b> <see cref="EveryComponentWithText_IsTracked"/> walks every
/// <c>.razor</c> in the library and fails if one carrying literal text appears in neither table. This is
/// not hypothetical bookkeeping: the first version of this file tracked five hand-picked paths and recorded
/// <c>UsersView</c> as migrated at zero — true of the 124-line wrapper, and false of the tabs it renders,
/// where <c>UsersListView</c> and <c>TeamsListView</c> held 81 untracked strings. A consumer would have
/// supplied a full translation table and still found that view in English, which is the complaint #204 was
/// filed about. A list somebody must remember to extend is how the number stops meaning anything.
/// </para>
/// </remarks>
public class TextCoverageTests
{
    /// <summary>Components fully migrated: these must stay at zero literal strings.</summary>
    private static readonly string[] Migrated =
    [
        "Features/Team/TeamSelector.razor",
        "Features/Authentication/LoginDisplay.razor",
        "Features/User/UsersView.razor",
        "Features/Audit/AuditLogView.razor",
        "Features/User/AssignOwnerDialog.razor",
        "Features/Team/SuspendedTeamNotice.razor",
        "Framework/ScopeOverrideEditor.razor",
        "Framework/RoleEditor.razor",
        "Features/Team/TeamDialog.razor",
        "Features/User/DirectoryOnlyUsersView.razor",
        "Features/User/UserIconDialog.razor",
        "Features/Team/TeamIconDialog.razor",
        "Features/Simulation/AccessSimulationCard.razor",
        "Features/Simulation/AccessSimulationBar.razor",
        "Features/Team/TeamInviteView.razor",
        "Features/Team/InviteUserDialog.razor",
        "Features/User/DeleteUserDialog.razor",
        "Features/User/TeamsListView.razor",
        "Features/User/UsersListView.razor",
        "Features/Team/TeamComponent.razor",

        // Written against the catalogue from the start rather than migrated into it, which is the point of
        // recording them here: a component that never had literal strings must not acquire its first one.
        "Features/Support/SupportCasesView.razor",
        "Features/Support/SupportQueueView.razor",
    ];

    /// <summary>
    /// Not yet migrated, with the count as it stands. These may only go down.
    /// </summary>
    /// <remarks>
    /// Baselined 2026-08-09 across the whole library, counting code-behind. The previous baseline covered
    /// two files and reported 104; the real figure was 376 — see the type-level remarks for how the gap
    /// arose and why discovery replaced the hand-written list.
    /// </remarks>
    private static readonly Dictionary<string, int> Pending = new()
    {
        ["Features/Api/ApiKeyRevealDialog.razor"] = 2,
        ["Features/Api/ApiKeyView.razor"] = 44,
        ["Features/Api/SystemApiKeyView.razor"] = 35,
        ["Features/Roles/TenantRoleManager.razor"] = 11,
        ["Features/Scopes/ScopeView.razor"] = 14,
        ["Features/Simulation/AccessSimulationDialog.razor"] = 12,
        ["Features/User/UserProfileView.razor"] = 13,
    };

    private static string ComponentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor");
    }

    /// <summary>
    /// The literal display strings in a component — its markup and its code-behind together.
    /// </summary>
    /// <remarks>
    /// <b>The code-behind half is not optional.</b> <c>AuditLogView</c> keeps 554 lines of C# in a
    /// <c>.razor.cs</c>, including every notification title it raises. Scanning only the <c>.razor</c>
    /// reported 43 where the component had 52, and the nine it missed — "Export failed", "Query failed" —
    /// are precisely the messages a user sees when something goes wrong.
    /// </remarks>
    private static int CountLiterals(string relativePath)
    {
        var full = Path.Combine(ComponentRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"'{relativePath}' was not found — the scan is pointed at the wrong place, or the file moved.");

        var count = TextScan.Count(File.ReadAllText(full));

        var codeBehind = full + ".cs";
        if (File.Exists(codeBehind)) count += TextScan.Count(File.ReadAllText(codeBehind));

        return count;
    }

    [Theory]
    [MemberData(nameof(MigratedComponents))]
    public void AMigratedComponent_RendersNoLiteralText(string relativePath)
    {
        var count = CountLiterals(relativePath);

        Assert.True(count == 0,
            $"'{relativePath}' is recorded as migrated but has {count} literal string(s). " +
            "Add a TextKey to the component's catalogue and resolve it through the TextSet, or a consumer " +
            "overriding the toolkit's wording will find this one still in English.");
    }

    [Theory]
    [MemberData(nameof(PendingComponents))]
    public void APendingComponent_DoesNotGrow(string relativePath, int allowed)
    {
        var count = CountLiterals(relativePath);

        Assert.True(count <= allowed,
            $"'{relativePath}' has {count} literal string(s), up from the recorded {allowed}. " +
            "#204 is a ratchet — this number may only go down. Route the new string through the text catalogue.");

        if (count < allowed)
        {
            // Not a failure of the code, but the record is now wrong and the next reader would trust it.
            Assert.Fail(
                $"'{relativePath}' is down to {count} literal string(s) from {allowed} — good, but update the " +
                "recorded count (or move it to Migrated if it is zero) so the remaining work stays accurate.");
        }
    }

    /// <summary>
    /// Every component carrying literal text must appear in one of the two tables above.
    /// </summary>
    /// <remarks>
    /// This is the test that would have caught the <c>UsersView</c> overstatement described in the type
    /// remarks. A component at zero needs no entry — there is nothing to track — but the moment one gains a
    /// literal string it must be recorded, so "not on the list" can never again read as "nothing to do".
    /// </remarks>
    [Fact]
    public void EveryComponentWithText_IsTracked()
    {
        var root = ComponentRoot();
        var tracked = new HashSet<string>(Migrated, StringComparer.OrdinalIgnoreCase);
        foreach (var path in Pending.Keys) tracked.Add(path);

        var untracked = new List<string>();

        foreach (var file in Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (tracked.Contains(relative)) continue;
            if (CountLiterals(relative) == 0) continue;

            untracked.Add(relative);
        }

        Assert.True(untracked.Count == 0,
            "These components render literal text and are in neither Migrated nor Pending, so the recorded " +
            "remaining work understates reality: " + string.Join(", ", untracked.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The scan can silently match nothing — a moved file, a broken regex — and every assertion above would
    /// pass while checking nothing. This proves it still finds what it is supposed to find.
    /// </summary>
    /// <remarks>
    /// <b>Proven against fixtures, not against production files.</b> An earlier version asserted that
    /// <c>AuditLogView</c> contained literals, which made the self-check fail the moment the migration
    /// succeeded — the test would have broken by the work being finished. Fixtures do not run out.
    /// </remarks>
    [Fact]
    public void TheScan_ActuallyFindsLiterals()
    {
        // The three categories the scan must see, and the four kinds of false positive it must not.
        Assert.Contains("Save changes", TextScan.Candidates("""<RadzenButton Text="Save changes" />"""));
        Assert.Contains("You are not a member.", TextScan.Candidates("<RadzenText>You are not a member.</RadzenText>"));
        Assert.Contains("Invitation sent", TextScan.Candidates("""NotificationService.Notify("Invitation sent");"""));

        Assert.Empty(TextScan.Candidates("""<RadzenButton Text="@_saveText" />"""));
        Assert.Empty(TextScan.Candidates("""<RadzenStack Orientation="Orientation.Horizontal" />"""));
        Assert.Empty(TextScan.Candidates("""<UsersListView ActionsTemplate="ActionsTemplate" />"""));
        Assert.Empty(TextScan.Candidates("""throw new InvalidOperationException("Unknown action here.");"""));
        Assert.Empty(TextScan.Candidates("/// <summary>Prose that is only documentation.</summary>"));

        // And it must still reach a real file at all — a path or glob mistake would otherwise leave every
        // count at zero, which reads as "all migrated". Driven by the Pending table rather than by a named
        // component: this assertion previously named AuditLogView, then TeamComponent, and broke both times
        // *because the migration succeeded*. Any entry still recorded as pending is by definition a file
        // that must scan above zero.
        var stillPending = Pending.OrderByDescending(x => x.Value).FirstOrDefault();
        if (stillPending.Key != null)
        {
            Assert.True(CountLiterals(stillPending.Key) > 0,
                $"The scan found no literal text in '{stillPending.Key}', which is recorded as having " +
                $"{stillPending.Value} — so the scan is broken rather than the record being stale.");
        }
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
        // A catalogue added after ThargaTextKeys was written must appear without anyone registering it -
        // that is the whole point of discovering by reflection, and UsersViewText is the first such case.
        Assert.Contains(all, k => k.Key == UsersViewText.TeamsTab.Key);
        Assert.All(all, k => Assert.False(string.IsNullOrWhiteSpace(k.Default)));

        // Every key of a newly added catalogue must arrive here, not just a sample of it: this is the list a
        // consumer generates their translation table from, so a key reflection misses is invisible to them
        // in exactly the way a literal string is.
        Assert.All(AuditLogViewText.All, key => Assert.Contains(all, k => k.Key == key.Key));
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
