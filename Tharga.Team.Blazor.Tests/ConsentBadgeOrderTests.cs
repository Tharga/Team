using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The "Not a member" badge is drawn before the access level it qualifies.
/// </summary>
/// <remarks>
/// A source scan, for the same reason <see cref="DialogButtonOrderTests"/> is one: badge order lives in
/// markup and nothing in the compiled component records it. Both the card header and the grid's Consent
/// column render the one fragment, so pinning it here covers every place a team badge appears.
/// <para>
/// Order carries meaning. Membership first reads as "not a member, but full access" — the qualifier
/// arriving before the level it qualifies. Reversed, the level lands as a plain statement of the caller's
/// standing and the correction comes too late to change it.
/// </para>
/// </remarks>
public class ConsentBadgeOrderTests
{
    // Matches the catalogue key rather than the English literal: the badge text moved into
    // TeamComponentText for #204, so scanning for "Not a member" would silently find nothing and the
    // order would stop being checked at all.
    private const string MembershipBadge = "TeamComponentText.NotAMember";
    private const string LevelBadge = "TeamVisibility.Label(";
    private const string ComponentPath = "Tharga.Team.Blazor/Features/Team/TeamComponent.razor";

    private static readonly Regex BadgeFragment = new(
        @"private RenderFragment ConsentBadges\(.*?</text>;",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetDirectories("Tharga.Team.Blazor").Any()) dir = dir.Parent;
        return dir;
    }

    private static string ConsentBadges()
    {
        var root = RepoRoot();
        Assert.NotNull(root);

        var markup = File.ReadAllText(Path.Combine(root.FullName, ComponentPath));
        var fragment = BadgeFragment.Match(markup);

        Assert.True(fragment.Success,
            $"{ComponentPath}: no ConsentBadges render fragment found — the guard is scanning nothing.");

        return fragment.Value;
    }

    private static (int Membership, int Level) Positions(string fragment) =>
        (fragment.IndexOf(MembershipBadge, StringComparison.Ordinal),
            fragment.IndexOf(LevelBadge, StringComparison.Ordinal));

    [Fact]
    public void MembershipIsDrawnBeforeTheAccessLevel()
    {
        var fragment = ConsentBadges();
        var (membership, level) = Positions(fragment);

        Assert.True(membership >= 0, $"{ComponentPath}: the 'Not a member' badge is gone from ConsentBadges.");
        Assert.True(level >= 0, $"{ComponentPath}: the access level badge is gone from ConsentBadges.");
        Assert.True(membership < level,
            $"{ComponentPath}: the 'Not a member' badge is drawn after the access level; it belongs to " +
            $"its left, so the qualifier is read before the level it qualifies.\n\n{fragment.Trim()}");
    }

    /// <summary>
    /// The self-check: the scan found a fragment carrying both badges. A source scan that silently
    /// matches nothing passes forever while reading as "the order is checked".
    /// </summary>
    [Fact]
    public void TheGuard_FindsBothBadges()
    {
        var (membership, level) = Positions(ConsentBadges());

        Assert.True(membership >= 0);
        Assert.True(level >= 0);
    }

    /// <summary>And that it actually rejects the shape it exists to catch.</summary>
    [Fact]
    public void TheGuard_DetectsTheReversedOrder()
    {
        const string reversed = """
            private RenderFragment ConsentBadges(ITeam<TMember> team) =>
            @<text>
                <RadzenStack Orientation="Orientation.Horizontal">
                    <RadzenBadge Text="@TeamVisibility.Label(ConsentOf(team))" />
                    <RadzenBadge Text="@_text[TeamComponentText.NotAMember]" />
                </RadzenStack>
            </text>;
            """;

        var fragment = BadgeFragment.Match(reversed);
        Assert.True(fragment.Success);

        var (membership, level) = Positions(fragment.Value);

        Assert.True(membership >= 0);       // found both...
        Assert.True(level >= 0);
        Assert.False(membership < level);   // ...and the order is wrong, so the guard above would fail
    }
}
