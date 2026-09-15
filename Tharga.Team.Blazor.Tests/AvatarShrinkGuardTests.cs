using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Any element sized from a <c>Size</c> parameter must also refuse to shrink.
/// </summary>
/// <remarks>
/// <b>This is a class of defect, not an incident — it has now been reported twice.</b> <c>UserAvatar</c>
/// was #279, fixed in #281; <c>TeamAvatar</c> was the identical defect, reported separately as #282 and
/// fixed here. Nothing connected the two at the time, so the second component kept the bug for two more
/// releases after the first was fixed.
/// <para>
/// The mechanism: an element with an inline <c>width</c> is still a flex item with <c>flex-shrink: 1</c>,
/// and its automatic minimum size is its min-content width. For an initials badge that is the width of two
/// letters, so the flex algorithm may take it well below the declared size — measured at 11.7 × 24 px
/// instead of 24 × 24 px on <c>UserAvatar</c>. The <c>&lt;img&gt;</c> branch resolves its minimum from the
/// specified width, so only the initials branch is visibly wrong; both are pinned anyway, so the two
/// branches of one component cannot drift apart.
/// </para>
/// <para>
/// Keyed on <c>width:@Size</c> — the shape of the defect rather than a component name — so a third avatar
/// written the same way is caught on the build that adds it rather than by a third bug report.
/// </para>
/// </remarks>
public class AvatarShrinkGuardTests
{
    private const string NoShrink = "flex-shrink:0";

    /// <summary>An inline width taken from the component's size parameter, spacing-insensitive.</summary>
    private static readonly Regex SizedFromParameter = new(@"width:\s*@Size", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static DirectoryInfo ComponentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir.FullName, "Tharga.Team.Blazor"));
    }

    /// <summary>Each element styled from a size parameter, and whether it also refuses to shrink.</summary>
    /// <remarks>
    /// One entry per <i>style attribute</i>, not per file: a component has an image branch and an initials
    /// branch, and checking the file as a whole would let one of them pass on the other's declaration.
    /// </remarks>
    private static (string File, string Style, bool Holds)[] SizedElements()
    {
        var root = ComponentRoot();

        return [.. root.GetFiles("*.razor", SearchOption.AllDirectories)
            .Select(f => (f, Text: File.ReadAllText(f.FullName)))
            .SelectMany(x => Regex.Matches(x.Text, @"style=""([^""]*)""")
                .Select(m => m.Groups[1].Value)
                .Where(style => SizedFromParameter.IsMatch(style))
                .Select(style => (
                    File: Path.GetRelativePath(root.FullName, x.f.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                    Style: style,
                    Holds: style.Replace(" ", string.Empty).Contains(NoShrink, StringComparison.Ordinal))))];
    }

    /// <summary>The guard.</summary>
    [Fact]
    public void EverySizedElement_RefusesToShrink()
    {
        var offenders = SizedElements().Where(x => !x.Holds).Select(x => x.File).Distinct().ToArray();

        Assert.True(offenders.Length == 0,
            $"These components size an element from @Size without '{NoShrink}', so it collapses toward its " +
            "content width inside a flex row: " + string.Join(", ", offenders) +
            $". Add {NoShrink} to the inline style, as UserAvatar and TeamAvatar do.");
    }

    /// <summary>
    /// The self-check. A scan matching nothing passes forever while reading as "every avatar is checked",
    /// so both known components and both of their branches must still be found.
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyFindsBothAvatars_AndBothBranches()
    {
        var found = SizedElements();

        Assert.Equal(2, found.Count(x => x.File.EndsWith("UserAvatar.razor", StringComparison.Ordinal)));
        Assert.Equal(2, found.Count(x => x.File.EndsWith("TeamAvatar.razor", StringComparison.Ordinal)));
    }

    /// <summary>And that it rejects the shape it exists to catch — TeamAvatar exactly as it was.</summary>
    [Fact]
    public void TheGuard_DetectsAnUnpinnedElement()
    {
        const string unpinned = "width:@Size;height:@Size;border-radius:6px;display:inline-flex;vertical-align:middle;";

        Assert.Matches(SizedFromParameter, unpinned);
        Assert.DoesNotContain(NoShrink, unpinned, StringComparison.Ordinal);
    }
}
