using Tharga.Team;

namespace Tharga.Team.Service.Tests;

public class ScopeRegistryTests
{
    [Fact]
    public void Owner_Gets_All_Scopes()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.Register("doc:delete", AccessLevel.Administrator);

        var scopes = registry.GetScopesForAccessLevel(AccessLevel.Owner);

        Assert.Equal(2, scopes.Count);
        Assert.Contains("doc:read", scopes);
        Assert.Contains("doc:delete", scopes);
    }

    [Fact]
    public void Administrator_Gets_All_Scopes()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.Register("doc:delete", AccessLevel.Administrator);

        var scopes = registry.GetScopesForAccessLevel(AccessLevel.Administrator);

        Assert.Equal(2, scopes.Count);
    }

    [Fact]
    public void User_Gets_User_And_Viewer_Scopes()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.Register("doc:download", AccessLevel.User);
        registry.Register("doc:delete", AccessLevel.Administrator);

        var scopes = registry.GetScopesForAccessLevel(AccessLevel.User);

        Assert.Equal(2, scopes.Count);
        Assert.Contains("doc:read", scopes);
        Assert.Contains("doc:download", scopes);
        Assert.DoesNotContain("doc:delete", scopes);
    }

    [Fact]
    public void Viewer_Gets_Only_Viewer_Scopes()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.Register("doc:download", AccessLevel.User);
        registry.Register("doc:delete", AccessLevel.Administrator);

        var scopes = registry.GetScopesForAccessLevel(AccessLevel.Viewer);

        Assert.Single(scopes);
        Assert.Contains("doc:read", scopes);
    }

    [Fact]
    public void Duplicate_Registration_Throws()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);

        Assert.Throws<InvalidOperationException>(() => registry.Register("doc:read", AccessLevel.User));
    }

    [Fact]
    public void Custom_Gets_No_Base_Scopes()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.Register("doc:download", AccessLevel.User);
        registry.Register("doc:delete", AccessLevel.Administrator);

        var scopes = registry.GetScopesForAccessLevel(AccessLevel.Custom);

        Assert.Empty(scopes);
    }

    [Fact]
    public void Custom_Ignores_Scope_Registered_At_Custom()
    {
        // Defensive: even if a scope is registered at Custom level, Custom resolves to no base scopes.
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.Register("doc:custom", AccessLevel.Custom);

        var scopes = registry.GetScopesForAccessLevel(AccessLevel.Custom);

        Assert.Empty(scopes);
    }

    [Fact]
    public void Custom_Effective_Scopes_Are_Roles_Union_Overrides_Only()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.Register("doc:delete", AccessLevel.Administrator);

        var roleRegistry = Substitute.For<ITenantRoleRegistry>();
        roleRegistry.GetScopesForRoles(Arg.Any<IEnumerable<string>>()).Returns(new[] { "role:scope" });
        registry.SetRoleRegistry(roleRegistry);

        var scopes = registry.GetEffectiveScopes(AccessLevel.Custom, new[] { "editor" }, new[] { "override:scope" });

        Assert.Equal(2, scopes.Count);
        Assert.Contains("role:scope", scopes);
        Assert.Contains("override:scope", scopes);
        Assert.DoesNotContain("doc:read", scopes);
        Assert.DoesNotContain("doc:delete", scopes);
    }

    [Fact]
    public void RegisterGrantOnly_Adds_A_Catalogue_Entry_Marked_GrantOnly()
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("case:read", "Read secrecy-classified case records.");

        var definition = Assert.Single(registry.All);
        Assert.Equal("case:read", definition.Name);
        Assert.Equal("Read secrecy-classified case records.", definition.Description);
        Assert.True(definition.GrantOnly);
    }

    [Fact]
    public void Register_Leaves_GrantOnly_False()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer, "View documents.");

        Assert.False(Assert.Single(registry.All).GrantOnly);
    }

    [Fact]
    public void RegisterGrantOnly_Duplicate_Throws()
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("case:read");

        Assert.Throws<InvalidOperationException>(() => registry.RegisterGrantOnly("case:read"));
    }

    [Fact]
    public void RegisterGrantOnly_Collides_With_An_Ordinary_Registration_Of_The_Same_Name()
    {
        var registry = new ScopeRegistry();
        registry.Register("case:read", AccessLevel.Administrator);

        Assert.Throws<InvalidOperationException>(() => registry.RegisterGrantOnly("case:read"));
    }

    [Fact]
    public void Register_Collides_With_A_GrantOnly_Registration_Of_The_Same_Name()
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("case:read");

        Assert.Throws<InvalidOperationException>(() => registry.Register("case:read", AccessLevel.Administrator));
    }

    [Theory]
    [InlineData(AccessLevel.Owner)]
    [InlineData(AccessLevel.Administrator)]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.Custom)]
    public void No_Access_Level_Grants_A_GrantOnly_Scope(AccessLevel accessLevel)
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("case:read");

        Assert.DoesNotContain("case:read", registry.GetScopesForAccessLevel(accessLevel));
    }

    [Fact]
    public void A_GrantOnly_Scope_Stays_In_The_Catalogue()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.RegisterGrantOnly("case:read");

        Assert.Contains(registry.All, s => s.Name == "case:read");
    }

    [Fact]
    public void A_GrantOnly_Scope_Does_Not_Disturb_Ordinary_Level_Resolution()
    {
        var registry = new ScopeRegistry();
        registry.Register("doc:read", AccessLevel.Viewer);
        registry.Register("doc:delete", AccessLevel.Administrator);
        registry.RegisterGrantOnly("case:read");

        var admin = registry.GetScopesForAccessLevel(AccessLevel.Administrator);
        var viewer = registry.GetScopesForAccessLevel(AccessLevel.Viewer);

        Assert.Equal(2, admin.Count);
        Assert.Contains("doc:read", admin);
        Assert.Contains("doc:delete", admin);
        Assert.Single(viewer);
        Assert.Contains("doc:read", viewer);
    }

    [Fact]
    public void A_GrantOnly_Scope_Is_Granted_By_A_Code_Registered_Role()
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("case:read");

        var roleRegistry = Substitute.For<ITenantRoleRegistry>();
        roleRegistry.GetScopesForRoles(Arg.Any<IEnumerable<string>>()).Returns(new[] { "case:read" });
        registry.SetRoleRegistry(roleRegistry);

        var scopes = registry.GetEffectiveScopes(AccessLevel.User, new[] { "CaseOfficer" });

        Assert.Contains("case:read", scopes);
    }

    [Fact]
    public void A_GrantOnly_Scope_Is_Granted_By_An_Explicit_Override()
    {
        var registry = new ScopeRegistry();
        registry.RegisterGrantOnly("case:read");

        var scopes = registry.GetEffectiveScopes(AccessLevel.User, null, new[] { "case:read" });

        Assert.Contains("case:read", scopes);
    }

    [Fact]
    public void An_Administrator_Without_The_Role_Does_Not_Hold_A_GrantOnly_Scope()
    {
        var registry = new ScopeRegistry();
        registry.Register("team:manage", AccessLevel.Administrator);
        registry.RegisterGrantOnly("case:read");

        var roleRegistry = Substitute.For<ITenantRoleRegistry>();
        roleRegistry.GetScopesForRoles(Arg.Any<IEnumerable<string>>()).Returns(Array.Empty<string>());
        registry.SetRoleRegistry(roleRegistry);

        var scopes = registry.GetEffectiveScopes(AccessLevel.Administrator, Array.Empty<string>());

        Assert.Contains("team:manage", scopes);
        Assert.DoesNotContain("case:read", scopes);
    }
}
