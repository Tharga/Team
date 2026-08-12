using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Any component reading the <b>full</b> user directory must decide on <c>users:manage</c> first.
/// </summary>
/// <remarks>
/// <b>This is a class of defect, not an incident.</b> <c>IUserService.GetAsync()</c> is gated by
/// <c>AuthorizationUserServiceDecorator</c> on the <c>users:manage</c> <i>system</i> scope, and no team
/// access level grants a system scope. A component that calls it without deciding first therefore throws
/// for every team-scoped caller — and because components resolve at render time, it surfaces as a broken
/// page rather than a failed startup.
/// <para>
/// It has happened twice. <b>#139</b> was <c>TeamComponent</c>: <i>"requires users:manage to render — /team
/// throws for any non-admin"</i>. <b>#222</b> was <c>AuditLogView</c>, which was never moved onto the
/// machinery that fixed #139 — and it error-paged the audit log for every customer organisation Owner at
/// Eplicta FortDocs.
/// </para>
/// <para>
/// A component satisfies this either by routing through <see cref="Features.User.UserDirectoryGate"/> — which
/// picks the co-member projection for a caller without the scope — or by gating its whole surface on the
/// scope and rendering something else without it, which is what the two <c>UsersView</c> tabs do.
/// </para>
/// <para>
/// The marker is <c>SystemUserScopes.Manage</c>, a symbol name rather than a display string. That is the
/// lesson from <c>ConsentBadgeOrderTests</c>, whose marker a later feature silently invalidated: a scan keyed
/// on something the codebase is actively rewording has an expiry date.
/// </para>
/// </remarks>
public class FullDirectoryReadGuardTests
{
    /// <remarks>
    /// Matched case-insensitively: the call appears both as <c>UserService.GetAsync()</c> through an
    /// injected property and as <c>userService.GetAsync()</c> through a local. A case-sensitive marker
    /// missed <c>AuditLogView</c> — the file this guard was written for.
    /// </remarks>
    private const string FullDirectoryRead = "UserService.GetAsync()";
    private const string DecisionMarker = "SystemUserScopes.Manage";

    private static readonly Regex ComponentFile = new(@"\.razor(\.cs)?$", RegexOptions.Compiled);

    private static DirectoryInfo ComponentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir.FullName, "Tharga.Team.Blazor"));
    }

    /// <summary>Component sources that read the full directory, with whether they decide on the scope.</summary>
    private static (string File, bool Decides)[] FullDirectoryReaders()
    {
        var root = ComponentRoot();

        return [.. root.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => ComponentFile.IsMatch(f.Name))
            .Select(f => (f, Text: File.ReadAllText(f.FullName)))
            .Where(x => x.Text.Contains(FullDirectoryRead, StringComparison.OrdinalIgnoreCase))
            .Select(x => (
                File: Path.GetRelativePath(root.FullName, x.f.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                Decides: x.Text.Contains(DecisionMarker, StringComparison.Ordinal)))];
    }

    /// <summary>The guard.</summary>
    [Fact]
    public void EveryFullDirectoryRead_DecidesOnTheSystemScope()
    {
        var offenders = FullDirectoryReaders().Where(x => !x.Decides).Select(x => x.File).ToArray();

        Assert.True(offenders.Length == 0,
            $"These components call {FullDirectoryRead} without deciding on '{DecisionMarker}' first, so " +
            "they throw for every team-scoped caller and the page fails to render: " +
            string.Join(", ", offenders) +
            ". Route through UserDirectoryGate, or gate the whole surface on the scope and render something " +
            "else without it.");
    }

    /// <summary>
    /// The self-check that matters: a scan matching nothing passes forever while reading as "every component
    /// is checked". Both known readers must still be found.
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyFindsTheReaders()
    {
        var readers = FullDirectoryReaders();

        Assert.True(readers.Length >= 3,
            $"Only {readers.Length} full-directory read(s) found; the marker has stopped matching the code " +
            "and the guard above is checking almost nothing.");

        Assert.Contains(readers, x => x.File.EndsWith("AuditLogView.razor.cs", StringComparison.Ordinal));
        Assert.Contains(readers, x => x.File.EndsWith("TeamComponent.razor", StringComparison.Ordinal));
    }

    /// <summary>And that it rejects the shape it exists to catch — #222 exactly as it was.</summary>
    [Fact]
    public void TheGuard_DetectsAnUndecidedRead()
    {
        const string undecided = """
            var userService = ServiceProvider.GetService<IUserService>();
            if (userService != null)
            {
                await foreach (var user in userService.GetAsync())
            """;

        Assert.Contains(FullDirectoryRead, undecided, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DecisionMarker, undecided, StringComparison.Ordinal);
    }
}
