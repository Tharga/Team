using System.Reflection;
using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Any UI source calling a <c>users:manage</c>-gated <see cref="IUserService"/> member must decide on the
/// scope first.
/// </summary>
/// <remarks>
/// <b>The generalisation of <see cref="FullDirectoryReadGuardTests"/>.</b> That guard covers one gated
/// member (<c>GetAsync</c>) on component files only, and both limits let the same defect back in:
/// <c>AccessSimulationState</c> is a plain class, not a component, and it called
/// <c>GetUserByKeyAsync</c>, not <c>GetAsync</c>. It carries <c>[RequireScope(SystemUserScopes.Manage)]</c>
/// just the same, so a team Owner opening <b>View as another user…</b> got
/// <c>UnauthorizedAccessException: GetUserByKeyAsync requires the 'users:manage' system scope</c> instead
/// of a member list — and the feature's own <c>simulation:use</c> is a <i>team</i> scope, which no caller
/// can ever pair with a system one.
/// <para>
/// <b>The gated set comes from reflection, not a list written here.</b> A hand-maintained list is a second
/// place stating what the attribute already says, and it silently stops covering the next member someone
/// gates. Marking a member <c>[RequireScope(SystemUserScopes.Manage)]</c> is what puts it in scope for this
/// guard.
/// </para>
/// <para>
/// A source satisfies the guard the same two ways as the narrower one: route through
/// <see cref="Features.User.UserDirectoryGate"/>, or gate the whole surface on the scope. Either leaves
/// <c>SystemUserScopes.Manage</c> in the file, which is the marker — a symbol name rather than a display
/// string, so rewording cannot quietly retire it.
/// </para>
/// </remarks>
public class GatedUserReadGuardTests
{
    private const string DecisionMarker = "SystemUserScopes.Manage";

    private static readonly Regex SourceFile = new(@"\.(razor|cs)$", RegexOptions.Compiled);

    /// <summary>
    /// Sources whose gated call is decided by whoever opens them, so the marker cannot be in the file.
    /// </summary>
    /// <remarks>
    /// <b>An exemption is a claim about another file, so it is written down rather than inferred.</b>
    /// <c>UserIconDialog</c> serves both the self-service and the administrative case, branching on its
    /// <c>AdminUserKey</c> parameter: null takes <c>SetOwnIconAsync</c>, set takes <c>SetUserIconAsync</c>.
    /// The only source that supplies it is <c>UsersListView</c>, which resolves
    /// <c>TeamScopeGate.HasSystemScope(..., SystemUserScopes.Manage)</c> before rendering anything. The
    /// decision is real; it is one file up.
    /// <para>
    /// <b>Do not add an entry to silence a failure.</b> Satisfying the marker with a comment mentioning the
    /// scope would pass this guard while deciding nothing, which is worse than no guard — that is why the
    /// exemption is a list here rather than something a source file can grant itself.
    /// </para>
    /// </remarks>
    private static readonly string[] DecidedByTheirOpener = ["Features/User/UserIconDialog.razor"];

    /// <summary>
    /// The <see cref="IUserService"/> members gated on <c>users:manage</c>, as the interface declares them.
    /// </summary>
    private static string[] GatedMembers()
        => [.. typeof(IUserService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<RequireScopeAttribute>()?.Scope == SystemUserScopes.Manage)
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)];

    private static DirectoryInfo SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir.FullName, "Tharga.Team.Blazor"));
    }

    /// <summary>Sources calling a gated member, with whether they decide on the scope.</summary>
    /// <remarks>
    /// Matched case-insensitively and against <c>UserService.&lt;member&gt;(</c> rather than the bare member
    /// name, so an unrelated method of the same name on another type does not register. The receiver
    /// appears both as an injected <c>UserService</c> property and as a <c>_userService</c> field, and both
    /// end in the same characters.
    /// </remarks>
    private static (string File, string Member, bool Decides)[] GatedReaders()
    {
        var root = SourceRoot();
        var gated = GatedMembers();

        return [.. root.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => SourceFile.IsMatch(f.Name))
            .Select(f => (f, Text: File.ReadAllText(f.FullName)))
            .SelectMany(x => gated
                .Where(member => x.Text.Contains($"UserService.{member}(", StringComparison.OrdinalIgnoreCase))
                .Select(member => (
                    File: Path.GetRelativePath(root.FullName, x.f.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                    Member: member,
                    Decides: x.Text.Contains(DecisionMarker, StringComparison.Ordinal))))];
    }

    /// <summary>The guard.</summary>
    [Fact]
    public void EveryGatedUserRead_DecidesOnTheSystemScope()
    {
        var offenders = GatedReaders()
            .Where(x => !x.Decides)
            .Where(x => !DecidedByTheirOpener.Contains(x.File, StringComparer.Ordinal))
            .Select(x => $"{x.File} ({x.Member})")
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"These sources call a '{SystemUserScopes.Manage}'-gated IUserService member without deciding " +
            $"on '{DecisionMarker}' first, so they throw for every team-scoped caller: " +
            string.Join(", ", offenders) +
            ". Route through UserDirectoryGate, or gate the whole surface on the scope.");
    }

    /// <summary>
    /// The self-check. A scan matching nothing passes forever while reading as "everything is checked",
    /// so both halves have to keep matching: the attribute still marks members, and the sources still
    /// call them.
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyFindsTheGatedMembersAndTheirCallers()
    {
        var gated = GatedMembers();

        Assert.Contains(nameof(IUserService.GetAsync), gated);
        Assert.Contains(nameof(IUserService.GetUserByKeyAsync), gated);

        var readers = GatedReaders();

        Assert.True(readers.Length >= 3,
            $"Only {readers.Length} gated read(s) found across {gated.Length} gated member(s); the marker " +
            "has stopped matching the code and the guard above is checking almost nothing.");
    }

    /// <summary>
    /// Every exemption still names a file that still makes a gated call. An exemption outliving the call
    /// it excused is a hole nobody opened deliberately.
    /// </summary>
    [Fact]
    public void EveryExemption_StillCoversALiveGatedCall()
    {
        var readers = GatedReaders();

        var stale = DecidedByTheirOpener
            .Where(file => !readers.Any(r => r.File.Equals(file, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(stale.Length == 0,
            "These exemptions no longer name a source that calls a gated member, so they excuse nothing " +
            "and should be deleted: " + string.Join(", ", stale));
    }

    /// <summary>
    /// And that it rejects the shape it exists to catch — the simulation picker exactly as it was.
    /// </summary>
    [Fact]
    public void TheGuard_DetectsAnUndecidedRead()
    {
        const string undecided = """
            var user = await _userService.GetUserByKeyAsync(member.Key);
            if (user == null) return member.Key;
            """;

        Assert.Contains($"UserService.{nameof(IUserService.GetUserByKeyAsync)}(", undecided, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DecisionMarker, undecided, StringComparison.Ordinal);
    }
}
