using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Where the profile menu's two built-in items navigate to — see <see cref="TeamMenuNavigation"/>.
/// </summary>
/// <remarks>
/// The defect: the toolkit ships <c>UserProfileView</c> and <c>TeamComponent</c> as components and lets the
/// host mount them at any route, but the menu navigated to the literals <c>profile</c> and <c>team</c>. A
/// host mounting either page elsewhere got a menu item leading to a 404 with no way to correct it.
/// <para>
/// It matters more than a stray link, because the profile page is where the access card's way *out* of a
/// reduced session lives. A broken route there is the difference between toggling demo mode off and having
/// to sign out.
/// </para>
/// </remarks>
public class TeamMenuNavigationTests
{
    /// <summary>The overwhelmingly common case: no host has set anything, and nothing changes.</summary>
    [Fact]
    public void Unset_KeepsTheBuiltInRoutes()
    {
        Assert.Equal("profile", TeamMenuNavigation.Resolve(null, TeamMenuNavigation.DefaultProfilePath));
        Assert.Equal("team", TeamMenuNavigation.Resolve(null, TeamMenuNavigation.DefaultTeamPath));
    }

    /// <summary>The ask: a host that moved the page points the menu item at it.</summary>
    [Theory]
    [InlineData("/account")]
    [InlineData("account")]
    [InlineData("/settings/me")]
    public void Configured_IsUsedVerbatim(string configured)
    {
        Assert.Equal(configured, TeamMenuNavigation.Resolve(configured, TeamMenuNavigation.DefaultProfilePath));
    }

    /// <summary>
    /// Whitespace is a configuration mistake, not a request to navigate nowhere. Treating it literally
    /// would break the menu in exactly the way this exists to prevent — and an empty string is what an
    /// unset config binding yields, which is the likeliest way to arrive here by accident.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void BlankIsTreatedAsUnset(string configured)
    {
        Assert.Equal("profile", TeamMenuNavigation.Resolve(configured, TeamMenuNavigation.DefaultProfilePath));
    }

    /// <summary>
    /// The two are independent. The pages are mounted separately, so moving one says nothing about the
    /// other — asserted because a single shared setting is the obvious "simplification" to make later.
    /// </summary>
    [Fact]
    public void TheTwoPathsDoNotAffectEachOther()
    {
        var options = new ThargaBlazorOptions { ProfilePath = "/account" };

        Assert.Equal("/account", TeamMenuNavigation.Resolve(options.ProfilePath, TeamMenuNavigation.DefaultProfilePath));
        Assert.Equal("team", TeamMenuNavigation.Resolve(options.TeamPath, TeamMenuNavigation.DefaultTeamPath));
    }

    /// <summary>Both options default to null, which is what makes this non-breaking.</summary>
    [Fact]
    public void TheOptionsDefaultToUnset()
    {
        var options = new ThargaBlazorOptions();

        Assert.Null(options.ProfilePath);
        Assert.Null(options.TeamPath);
    }

    /// <summary>
    /// The self-check: every assertion above would still pass if <c>Resolve</c> ignored its first argument
    /// and always returned the default.
    /// </summary>
    [Fact]
    public void TheConfiguredValueIsWhatDecides()
    {
        Assert.NotEqual(
            TeamMenuNavigation.Resolve(null, TeamMenuNavigation.DefaultProfilePath),
            TeamMenuNavigation.Resolve("/account", TeamMenuNavigation.DefaultProfilePath));
    }
}
