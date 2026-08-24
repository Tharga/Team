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
}
