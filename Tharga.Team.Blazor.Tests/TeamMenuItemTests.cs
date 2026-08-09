using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Host-supplied profile-menu items: registration, and who sees them.
/// </summary>
/// <remarks>
/// The visibility gate is <b>rendering only</b>. These tests pin that it hides a link the caller cannot use —
/// they do not, and must not be read to, assert that the target page is protected. The page gates itself.
/// </remarks>
public class TeamMenuItemTests
{
    private static ClaimsPrincipal Caller(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static TeamMenuItem Item(string scope = null, string role = null)
        => new("help", new TextKey("myapp.menu.help", "Help"), "/help", scope, role);

    [Fact]
    public void AddMenuItem_RecordsIconTextAndHref()
    {
        var options = new ThargaBlazorOptions();
        options.AddMenuItem("help", "myapp.menu.help", "Help", "/help");

        var item = Assert.Single(options._menuItems);
        Assert.Equal("help", item.Icon);
        Assert.Equal("myapp.menu.help", item.Text.Key);
        Assert.Equal("Help", item.Text.Default);
        Assert.Equal("/help", item.Href);
    }

    [Fact]
    public void AddMenuItem_KeepsRegistrationOrder()
    {
        var options = new ThargaBlazorOptions();
        options.AddMenuItem("help", "a", "A", "/a");
        options.AddMenuItem("info", "b", "B", "/b");

        Assert.Equal(["a", "b"], options._menuItems.Select(x => x.Text.Key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddMenuItem_RefusesAnUnusableIconKeyOrHref(string bad)
    {
        var options = new ThargaBlazorOptions();

        Assert.ThrowsAny<ArgumentException>(() => options.AddMenuItem(bad, "k", "T", "/x"));
        Assert.ThrowsAny<ArgumentException>(() => options.AddMenuItem("help", bad, "T", "/x"));
        Assert.ThrowsAny<ArgumentException>(() => options.AddMenuItem("help", "k", "T", bad));
    }

    /// <summary>An ungated item is the common case and must not depend on the caller at all.</summary>
    [Fact]
    public void AnUngatedItem_IsVisibleToAnyone()
    {
        Assert.True(TeamMenuItemVisibility.IsVisible(Caller(), Item()));
        Assert.True(TeamMenuItemVisibility.IsVisible(Anonymous(), Item()));
        Assert.True(TeamMenuItemVisibility.IsVisible(null, Item()));
    }

    [Fact]
    public void AScopedItem_NeedsThatScope()
    {
        var item = Item(scope: "audit:read");

        Assert.True(TeamMenuItemVisibility.IsVisible(Caller(new Claim(TeamClaimTypes.Scope, "audit:read")), item));
        Assert.False(TeamMenuItemVisibility.IsVisible(Caller(new Claim(TeamClaimTypes.Scope, "team:read")), item));
        Assert.False(TeamMenuItemVisibility.IsVisible(Caller(), item));
    }

    /// <summary>
    /// A system grant counts. A cross-team administrator holds the scope as a system claim, and hiding their
    /// link because it arrived by the other provenance would be wrong.
    /// </summary>
    [Fact]
    public void ASystemScope_AlsoSatisfiesAScopedItem()
    {
        var item = Item(scope: "audit:read");

        Assert.True(TeamMenuItemVisibility.IsVisible(Caller(new Claim(TeamClaimTypes.SystemScope, "audit:read")), item));
    }

    [Fact]
    public void ARoleGatedItem_NeedsThatRole()
    {
        var item = Item(role: "Developer");

        Assert.True(TeamMenuItemVisibility.IsVisible(Caller(new Claim(ClaimTypes.Role, "Developer")), item));
        Assert.False(TeamMenuItemVisibility.IsVisible(Caller(new Claim(ClaimTypes.Role, "Support")), item));
    }

    /// <summary>Both gates set means both must hold — the narrower reading, and the safer default.</summary>
    [Fact]
    public void BothGates_MustHold()
    {
        var item = Item(scope: "audit:read", role: "Developer");

        Assert.False(TeamMenuItemVisibility.IsVisible(Caller(new Claim(TeamClaimTypes.Scope, "audit:read")), item));
        Assert.False(TeamMenuItemVisibility.IsVisible(Caller(new Claim(ClaimTypes.Role, "Developer")), item));
        Assert.True(TeamMenuItemVisibility.IsVisible(
            Caller(new Claim(TeamClaimTypes.Scope, "audit:read"), new Claim(ClaimTypes.Role, "Developer")), item));
    }

    /// <summary>A gated item is never shown to someone not signed in, whatever claims a bare identity carries.</summary>
    [Fact]
    public void AGatedItem_IsHiddenFromAnAnonymousCaller()
    {
        Assert.False(TeamMenuItemVisibility.IsVisible(Anonymous(), Item(scope: "audit:read")));
        Assert.False(TeamMenuItemVisibility.IsVisible(null, Item(role: "Developer")));
    }
}
