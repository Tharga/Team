using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Hiding the scopes and roles of a feature that is switched off for a team, in the scope and role
/// pickers, without losing grants the principal already holds.
/// </summary>
public class ScopeVisibilityTests
{
    private static readonly IReadOnlySet<string> Hidden = new HashSet<string> { "atlas:manage", "firewall:manage" };

    [Fact]
    public async Task HiddenAsync_ReturnsTheScopesTheProviderHidesForTheTeam()
    {
        var provider = new Mock<ITenantScopeVisibilityProvider>();
        provider.Setup(x => x.IsScopeVisibleAsync("T1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string scope, CancellationToken _) => !scope.StartsWith("atlas:"));

        var hidden = await ScopeVisibility.HiddenAsync(provider.Object, "T1", ["content:read", "atlas:read", "atlas:manage"]);

        Assert.Equal(["atlas:manage", "atlas:read"], hidden.Order());
    }

    [Fact]
    public async Task HiddenAsync_HidesNothing_WithoutAProviderOrATeam()
    {
        var provider = new Mock<ITenantScopeVisibilityProvider>();
        provider.Setup(x => x.IsScopeVisibleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Empty(await ScopeVisibility.HiddenAsync(null, "T1", ["a"]));
        Assert.Empty(await ScopeVisibility.HiddenAsync(provider.Object, null, ["a"]));
    }

    [Fact]
    public void Options_LeaveOutHiddenScopes_EvenWhenInheritedOrHeld()
    {
        var options = ScopeVisibility.Options(
            grantable: ["content:read", "atlas:manage"],
            inherited: ["firewall:manage"],
            overrides: ["atlas:manage", "issue:read"],
            hidden: Hidden);

        Assert.Equal(["content:read", "issue:read"], options);
    }

    [Fact]
    public void Checked_LeavesOutHiddenScopes()
    {
        var value = ScopeVisibility.Checked(inherited: ["content:read", "firewall:manage"], overrides: ["atlas:manage"], hidden: Hidden);

        Assert.Equal(["content:read"], value);
    }

    [Fact]
    public void Persist_KeepsAHiddenOverrideTheUserCouldNotSee()
    {
        // The principal held atlas:manage before Atlas was switched off for the team. Editing its other
        // overrides now must not silently drop it; switching Atlas on again should find it still there.
        var persisted = ScopeVisibility.Persist(
            selected: ["content:read", "issue:read"],
            inherited: ["content:read"],
            previousOverrides: ["atlas:manage", "issue:write"],
            hidden: Hidden);

        Assert.Equal(["issue:read", "atlas:manage"], persisted);
    }

    [Fact]
    public void Persist_ForACustomRole_KeepsTheScopesItHadThatAreNowHidden()
    {
        // TenantRoleManager: a role has no inherited scopes. Editing an "Atlas operator" role's visible
        // scopes while Atlas is off must not strip its atlas:manage.
        var scopes = ScopeVisibility.Persist(
            selected: ["content:read"],
            inherited: [],
            previousOverrides: ["content:read", "content:write", "atlas:manage"],
            hidden: Hidden);

        Assert.Equal(["content:read", "atlas:manage"], scopes);
    }

    [Fact]
    public void Persist_WithNothingHidden_IsTheSelectionMinusInherited()
    {
        var persisted = ScopeVisibility.Persist(["a", "b", "c"], inherited: ["a"], previousOverrides: ["z"], hidden: null);

        Assert.Equal(["b", "c"], persisted);
    }

    [Fact]
    public async Task VisibleRolesAsync_FiltersByTheRoleProvider()
    {
        var provider = new Mock<ITenantRoleVisibilityProvider>();
        provider.Setup(x => x.IsRoleVisibleAsync("T1", "AtlasAdmin", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        provider.Setup(x => x.IsRoleVisibleAsync("T1", "Editor", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        TenantRoleDefinition[] roles = [new("AtlasAdmin", ["atlas:manage"]), new("Editor", ["content:write"])];

        var visible = await ScopeVisibility.VisibleRolesAsync(provider.Object, "T1", roles);

        Assert.Equal(["Editor"], visible.Select(x => x.Name));
    }
}
