using Tharga.Team;
using Tharga.Team.Blazor.Features.Scopes;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Tests for <see cref="ScopeReference.Build"/> — the dynamic projection of the configured scope model
/// (scope → description, granting access levels, granting tenant roles) that <c>ScopeView</c> renders.
/// </summary>
public class ScopeReferenceTests
{
    private static (ScopeRegistry scopes, TenantRoleRegistry roles) BuildRegistries()
    {
        var scopes = new ScopeRegistry();
        scopes.Register("orders:read", AccessLevel.Viewer, "View orders and details.");
        scopes.Register("orders:write", AccessLevel.User, "Create and edit orders.");
        scopes.Register("orders:refund", AccessLevel.Administrator, "Issue refunds.");

        var roles = new TenantRoleRegistry();
        roles.Register("Support", "orders:read");
        roles.Register("Editor", ["orders:write", "orders:refund"], "Content editors.");

        return (scopes, roles);
    }

    private static ScopeRow Row(IReadOnlyList<ScopeRow> rows, string name) => rows.Single(r => r.Name == name);

    [Fact]
    public void GrantOnlyScope_AppearsInTheCatalogue_WithItsDescription()
    {
        var (scopes, roles) = BuildRegistries();
        scopes.RegisterGrantOnly("case:read", "Read secrecy-classified case records.");

        var row = Row(ScopeReference.Build(scopes, roles), "case:read");

        Assert.Equal("Read secrecy-classified case records.", row.Description);
        Assert.True(row.GrantOnly);
    }

    [Fact]
    public void GrantOnlyScope_IsGrantedByNoAccessLevel()
    {
        var (scopes, roles) = BuildRegistries();
        scopes.RegisterGrantOnly("case:read");

        var row = Row(ScopeReference.Build(scopes, roles), "case:read");

        Assert.Empty(row.AccessLevels);
    }

    [Fact]
    public void GrantOnlyScope_StillNamesTheRolesThatGrantIt()
    {
        var scopes = new ScopeRegistry();
        scopes.RegisterGrantOnly("case:read");

        var roles = new TenantRoleRegistry();
        roles.Register("CaseOfficer", "case:read");

        var row = Row(ScopeReference.Build(scopes, roles), "case:read");

        Assert.Equal(["CaseOfficer"], row.Roles);
    }

    [Fact]
    public void OrdinaryScope_IsNotMarkedGrantOnly()
    {
        var (scopes, roles) = BuildRegistries();

        Assert.False(Row(ScopeReference.Build(scopes, roles), "orders:read").GrantOnly);
    }

    [Fact]
    public void ViewerLevelScope_IsGrantedToAllLevels()
    {
        var (scopes, roles) = BuildRegistries();

        var row = Row(ScopeReference.Build(scopes, roles), "orders:read");

        Assert.Equal(
            new[] { AccessLevel.Owner, AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer },
            row.AccessLevels);
    }

    [Fact]
    public void UserLevelScope_IsGrantedToOwnerAdminUser_NotViewer()
    {
        var (scopes, roles) = BuildRegistries();

        var row = Row(ScopeReference.Build(scopes, roles), "orders:write");

        Assert.Equal(
            new[] { AccessLevel.Owner, AccessLevel.Administrator, AccessLevel.User },
            row.AccessLevels);
    }

    [Fact]
    public void AdministratorLevelScope_IsGrantedToOwnerAndAdminOnly()
    {
        var (scopes, roles) = BuildRegistries();

        var row = Row(ScopeReference.Build(scopes, roles), "orders:refund");

        Assert.Equal(new[] { AccessLevel.Owner, AccessLevel.Administrator }, row.AccessLevels);
    }

    [Fact]
    public void Roles_AreListedOnlyForTheScopesTheyGrant()
    {
        var (scopes, roles) = BuildRegistries();

        var rows = ScopeReference.Build(scopes, roles);

        Assert.Equal(new[] { "Support" }, Row(rows, "orders:read").Roles);
        Assert.Equal(new[] { "Editor" }, Row(rows, "orders:write").Roles);
        Assert.Equal(new[] { "Editor" }, Row(rows, "orders:refund").Roles);
    }

    [Fact]
    public void Description_FlowsThrough()
    {
        var (scopes, roles) = BuildRegistries();

        Assert.Equal("View orders and details.", Row(ScopeReference.Build(scopes, roles), "orders:read").Description);
    }

    [Fact]
    public void Rows_AreOrdinalSortedByName()
    {
        var (scopes, roles) = BuildRegistries();

        var names = ScopeReference.Build(scopes, roles).Select(r => r.Name).ToArray();

        Assert.Equal(new[] { "orders:read", "orders:refund", "orders:write" }, names);
    }

    [Fact]
    public void NullScopeRegistry_ReturnsEmpty()
    {
        Assert.Empty(ScopeReference.Build(null, null));
    }

    [Fact]
    public void NullRoleRegistry_YieldsEmptyRoleLists_NoThrow()
    {
        var (scopes, _) = BuildRegistries();

        var rows = ScopeReference.Build(scopes, null);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Empty(r.Roles));
    }

    [Theory]
    [InlineData(AccessLevel.Owner, AccessLevel.Administrator)]   // Owner collapses to Administrator
    [InlineData(AccessLevel.Administrator, AccessLevel.Administrator)]
    [InlineData(AccessLevel.User, AccessLevel.User)]
    [InlineData(AccessLevel.Viewer, AccessLevel.Viewer)]
    public void ToSelectableLevel_MapsAsExpected(AccessLevel actual, AccessLevel expected)
        => Assert.Equal(expected, ScopeReference.ToSelectableLevel(actual));

    [Fact]
    public void ToSelectableLevel_Custom_IsNull()
        => Assert.Null(ScopeReference.ToSelectableLevel(AccessLevel.Custom));

    [Fact]
    public void Resolve_ByLevel_WhenSelectedLevelGrantsTheScope()
    {
        var (scopes, roles) = BuildRegistries();
        var row = Row(ScopeReference.Build(scopes, roles), "orders:write"); // User-level scope

        var grant = ScopeReference.Resolve(row, AccessLevel.User, new HashSet<string>(), new HashSet<string>());

        Assert.True(grant.Granted);
        Assert.True(grant.ByLevel);
        Assert.Empty(grant.ByRoles);
        Assert.False(grant.ByOverride);
    }

    [Fact]
    public void Resolve_NotByLevel_ButByRole()
    {
        var (scopes, roles) = BuildRegistries();
        var row = Row(ScopeReference.Build(scopes, roles), "orders:refund"); // Administrator-level scope

        var grant = ScopeReference.Resolve(row, AccessLevel.User, new HashSet<string> { "Editor" }, new HashSet<string>());

        Assert.True(grant.Granted);
        Assert.False(grant.ByLevel);
        Assert.Equal(new[] { "Editor" }, grant.ByRoles);
    }

    [Fact]
    public void Resolve_ByOverride_Only()
    {
        var (scopes, roles) = BuildRegistries();
        var row = Row(ScopeReference.Build(scopes, roles), "orders:refund");

        var grant = ScopeReference.Resolve(row, AccessLevel.Viewer, new HashSet<string>(), new HashSet<string> { "orders:refund" });

        Assert.True(grant.Granted);
        Assert.False(grant.ByLevel);
        Assert.Empty(grant.ByRoles);
        Assert.True(grant.ByOverride);
    }

    [Fact]
    public void Resolve_NotGranted_WhenNothingSelected()
    {
        var (scopes, roles) = BuildRegistries();
        var row = Row(ScopeReference.Build(scopes, roles), "orders:refund");

        var grant = ScopeReference.Resolve(row, null, new HashSet<string>(), new HashSet<string>());

        Assert.False(grant.Granted);
    }

    private static SystemScopeRegistry BuildSystemRegistry()
    {
        var system = new SystemScopeRegistry();
        system.Register("system:teams:read", "Read any team.");
        system.Register("system:metrics:read", "Read metrics.");
        system.Register("mcp:discover", "Discover MCP.");
        return system;
    }

    [Fact]
    public void UserSystemScopes_ReturnsOnlyHeldRegisteredSystemScopes_OrdinalSorted()
    {
        // Holds two system scopes plus an unrelated team-scope claim.
        var result = ScopeReference.UserSystemScopes(
            BuildSystemRegistry(), new[] { "system:teams:read", "mcp:discover", "orders:read" });

        Assert.Equal(new[] { "mcp:discover", "system:teams:read" }, result.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void UserSystemScopes_IgnoresClaimsThatAreNotRegisteredSystemScopes()
    {
        var result = ScopeReference.UserSystemScopes(BuildSystemRegistry(), new[] { "orders:read", "team:manage" });

        Assert.Empty(result);
    }

    [Fact]
    public void UserSystemScopes_CarriesDescription()
    {
        var result = ScopeReference.UserSystemScopes(BuildSystemRegistry(), new[] { "mcp:discover" });

        Assert.Equal("Discover MCP.", result.Single().Description);
    }

    [Fact]
    public void UserSystemScopes_NullRegistry_ReturnsEmpty()
        => Assert.Empty(ScopeReference.UserSystemScopes(null, new[] { "mcp:discover" }));

    [Fact]
    public void UserSystemScopes_NoneHeld_ReturnsEmpty()
        => Assert.Empty(ScopeReference.UserSystemScopes(BuildSystemRegistry(), Array.Empty<string>()));
}
