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

    // ---- Building from a role list rather than the registry (#292) ----

    /// <summary>
    /// A team's custom role credits the scopes it names, exactly as a code-registered one does.
    /// </summary>
    /// <remarks>
    /// The registry-taking overload cannot express this: <see cref="ITenantRoleRegistry"/> holds
    /// code-registered roles only, and a team's own roles need an async per-team read. Taking the resolved
    /// list is what lets the page show what a member actually holds.
    /// </remarks>
    [Fact]
    public void BuildFromRoleList_CreditsACustomRole()
    {
        var (scopes, _) = BuildRegistries();
        var roles = new List<TenantRoleDefinition>
        {
            new("Support", ["orders:read"], null),
            new("Refunder", ["orders:refund"], "Defined by the team at runtime.")
        };

        var rows = ScopeReference.BuildForRoles(scopes, roles);

        Assert.Equal(["Refunder"], Row(rows, "orders:refund").Roles);
        Assert.Equal(["Support"], Row(rows, "orders:read").Roles);
    }

    /// <summary>
    /// A scope granted by <b>no access level</b> and only by a custom role is still credited to it.
    /// </summary>
    /// <remarks>
    /// This is the shape behind the reported symptom: the member holds the scope through the role, the
    /// claims pipeline resolves it correctly, and the page used to show nothing granting it at all.
    /// </remarks>
    [Fact]
    public void BuildFromRoleList_CreditsAGrantOnlyScopeToTheCustomRoleThatNamesIt()
    {
        var (scopes, _) = BuildRegistries();
        scopes.RegisterGrantOnly("case:read", "Read secrecy-classified case records.");

        var rows = ScopeReference.BuildForRoles(scopes, [new TenantRoleDefinition("CaseOfficer", ["case:read"], null)]);
        var row = Row(rows, "case:read");

        Assert.Equal(["CaseOfficer"], row.Roles);
        Assert.Empty(row.AccessLevels);
        Assert.True(row.GrantOnly);
    }

    /// <summary>The registry-taking overload keeps behaving exactly as it did.</summary>
    [Fact]
    public void BuildFromRegistry_MatchesBuildForItsOwnRoleList()
    {
        var (scopes, roles) = BuildRegistries();

        var fromRegistry = ScopeReference.Build(scopes, roles);
        var fromList = ScopeReference.BuildForRoles(scopes, roles.All);

        Assert.Equal(fromRegistry.Select(x => x.Name), fromList.Select(x => x.Name));
        Assert.Equal(fromRegistry.Select(x => string.Join(",", x.Roles)), fromList.Select(x => string.Join(",", x.Roles)));
    }

    /// <summary>
    /// <b>The regression guard for #292.</b> A member assigned a team's custom role has it preselected, and
    /// the scope it grants resolves as granted — not greyed out.
    /// </summary>
    /// <remarks>
    /// Walks the chain the page walks: resolved roles → rows → the member's preselected roles → the grant.
    /// Asserting only one link would miss the defect, which was that the *narrowing* used a different role
    /// list from the *crediting*.
    /// </remarks>
    [Fact]
    public void AMemberHoldingACustomRole_IsCreditedWithItsScopes()
    {
        var (scopes, _) = BuildRegistries();
        IReadOnlyList<TenantRoleDefinition> teamRoles =
        [
            new("Support", ["orders:read"], null),
            new("Refunder", ["orders:refund"], "Defined by the team at runtime.")
        ];

        var rows = ScopeReference.BuildForRoles(scopes, teamRoles);
        var selected = ScopeReference.PreselectedRoles(["Refunder"], teamRoles);
        var grant = ScopeReference.Resolve(Row(rows, "orders:refund"), AccessLevel.Viewer, new HashSet<string>(selected), new HashSet<string>());

        Assert.Equal(["Refunder"], selected);
        Assert.True(grant.Granted);
        Assert.Equal(["Refunder"], grant.ByRoles);
        Assert.False(grant.ByLevel);
    }

    /// <summary>
    /// The same member against the code-registered roles alone — the old behaviour, kept as a test so the
    /// defect is described rather than just fixed.
    /// </summary>
    [Fact]
    public void TheSameMember_AgainstCodeRolesOnly_LosesTheRoleAndTheScope()
    {
        var (scopes, codeRoles) = BuildRegistries();

        var rows = ScopeReference.BuildForRoles(scopes, codeRoles.All);
        var selected = ScopeReference.PreselectedRoles(["Refunder"], codeRoles.All);
        var grant = ScopeReference.Resolve(Row(rows, "orders:refund"), AccessLevel.Viewer, new HashSet<string>(selected), new HashSet<string>());

        Assert.Empty(selected);
        Assert.False(grant.Granted);
    }

    /// <summary>An assignment naming a role the team no longer defines is dropped — there is nothing to credit.</summary>
    [Fact]
    public void PreselectedRoles_DropsAnAssignmentWithNoMatchingDefinition()
        => Assert.Equal(["Support"], ScopeReference.PreselectedRoles(
            ["Support", "Deleted"],
            [new TenantRoleDefinition("Support", ["orders:read"], null)]));

    [Fact]
    public void PreselectedRoles_WithNoAssignments_IsEmpty()
        => Assert.Empty(ScopeReference.PreselectedRoles(null, [new TenantRoleDefinition("Support", [], null)]));

    /// <summary>No roles at all is not an error — every scope simply has none crediting it.</summary>
    [Fact]
    public void BuildFromRoleList_WithNoRoles_CreditsNothing()
    {
        var (scopes, _) = BuildRegistries();

        var rows = ScopeReference.BuildForRoles(scopes, null);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Empty(r.Roles));
    }
}
