using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The "Not a member" marker is drawn before the access level it qualifies, in the grid's "Your access" cell.
/// </summary>
/// <remarks>
/// A source scan, for the same reason <see cref="DialogButtonOrderTests"/> is one: badge order lives in
/// markup and nothing in the compiled component records it. The "Your access" cell is the one place both
/// appear together; the card header carries the membership badge alone.
/// <para>
/// Order carries meaning. Membership first reads as "not a member, but Administrator" — the qualifier
/// arriving before the level it qualifies. Reversed, the level lands as a plain statement of the caller's
/// standing and the correction comes too late to change it.
/// </para>
/// </remarks>
public class ConsentBadgeOrderTests
{
    // Matches the catalogue key rather than the English literal: the text lives in TeamComponentText, so
    // scanning for "Not a member" would silently find nothing and the order would stop being checked at all.
    private const string MembershipMarker = "TeamComponentText.NotAMember";
    private const string LevelBadge = "AccessLevelBadge.Text(";
    private const string ComponentPath = "Tharga.Team.Blazor/Features/Team/TeamComponent.razor";

    private static readonly Regex CellFragment = new(
        @"private RenderFragment YourAccessCell\(.*?</text>;",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetDirectories("Tharga.Team.Blazor").Any()) dir = dir.Parent;
        return dir;
    }

    private static string YourAccessCell()
    {
        var root = RepoRoot();
        Assert.NotNull(root);

        var markup = File.ReadAllText(Path.Combine(root.FullName, ComponentPath));
        var fragment = CellFragment.Match(markup);

        Assert.True(fragment.Success,
            $"{ComponentPath}: no YourAccessCell render fragment found — the guard is scanning nothing.");

        return fragment.Value;
    }

    private static (int Membership, int Level) Positions(string fragment) =>
        (fragment.IndexOf(MembershipMarker, StringComparison.Ordinal),
            fragment.IndexOf(LevelBadge, StringComparison.Ordinal));

    [Fact]
    public void MembershipIsDrawnBeforeTheAccessLevel()
    {
        var fragment = YourAccessCell();
        var (membership, level) = Positions(fragment);

        Assert.True(membership >= 0, $"{ComponentPath}: the 'Not a member' marker is gone from YourAccessCell.");
        Assert.True(level >= 0, $"{ComponentPath}: the access level badge is gone from YourAccessCell.");
        Assert.True(membership < level,
            $"{ComponentPath}: the 'Not a member' marker is drawn after the access level; it belongs to " +
            $"its left, so the qualifier is read before the level it qualifies.\n\n{fragment.Trim()}");
    }

    /// <summary>
    /// The self-check: the scan found a fragment carrying both. A source scan that silently matches
    /// nothing passes forever while reading as "the order is checked".
    /// </summary>
    [Fact]
    public void TheGuard_FindsBoth()
    {
        var (membership, level) = Positions(YourAccessCell());

        Assert.True(membership >= 0);
        Assert.True(level >= 0);
    }

    /// <summary>And that it actually rejects the shape it exists to catch.</summary>
    [Fact]
    public void TheGuard_DetectsTheReversedOrder()
    {
        const string reversed = """
            private RenderFragment YourAccessCell(ITeam<TMember> team) =>
            @<text>
                <RadzenStack Orientation="Orientation.Horizontal">
                    <RadzenBadge Text="@AccessLevelBadge.Text(myLevel, TextSet.Empty)" />
                    <RadzenIcon Icon="person_off" title="@_text[TeamComponentText.NotAMember]" />
                </RadzenStack>
            </text>;
            """;

        var fragment = CellFragment.Match(reversed);
        Assert.True(fragment.Success);

        var (membership, level) = Positions(fragment.Value);

        Assert.True(membership >= 0);       // found both...
        Assert.True(level >= 0);
        Assert.False(membership < level);   // ...and the order is wrong, so the guard above would fail
    }
}
