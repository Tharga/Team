using Tharga.Team;
using Tharga.Team.Blazor.Features.Roles;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Editing working-set behind the <c>TenantRoleManager</c> component (Tharga/Team#117):
/// load/add/remove/validate/project custom roles.
/// </summary>
public class TenantRoleManagerModelTests
{
    [Fact]
    public void Load_then_ToDefinitions_round_trips_roles()
    {
        var model = new TenantRoleManagerModel();
        model.Load([new TenantRoleDefinition("Registrar", ["case:read", "case:write"], "Registers cases")]);

        var definitions = model.ToDefinitions();

        var role = Assert.Single(definitions);
        Assert.Equal("Registrar", role.Name);
        Assert.Equal(["case:read", "case:write"], role.Scopes);
        Assert.Equal("Registers cases", role.Description);
    }

    [Fact]
    public void AddNew_and_Remove_mutate_the_working_set()
    {
        var model = new TenantRoleManagerModel();
        var added = model.AddNew();
        added.Name = "Reader";
        added.Scopes = ["case:read"];

        Assert.Single(model.Roles);

        model.Remove(added);
        Assert.Empty(model.Roles);
    }

    [Fact]
    public void ToDefinition_trims_name_and_nulls_blank_description()
    {
        var model = new TenantRoleManagerModel();
        var role = model.AddNew();
        role.Name = "  Reader  ";
        role.Description = "   ";
        role.Scopes = ["case:read"];

        var definition = Assert.Single(model.ToDefinitions());
        Assert.Equal("Reader", definition.Name);
        Assert.Null(definition.Description);
    }

    [Fact]
    public void Validate_flags_empty_names()
    {
        var model = new TenantRoleManagerModel();
        model.AddNew().Name = "   ";

        var errors = model.Validate([]);

        Assert.Contains(errors, e => e.Contains("empty"));
    }

    [Fact]
    public void Validate_flags_duplicate_names()
    {
        var model = new TenantRoleManagerModel();
        model.AddNew().Name = "Reader";
        model.AddNew().Name = "Reader";

        var errors = model.Validate([]);

        Assert.Contains(errors, e => e.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_flags_collision_with_code_role()
    {
        var model = new TenantRoleManagerModel();
        model.AddNew().Name = "Developer";

        var errors = model.Validate(["Developer"]);

        Assert.Contains(errors, e => e.Contains("built-in"));
    }

    [Fact]
    public void Validate_passes_for_a_clean_set()
    {
        var model = new TenantRoleManagerModel();
        var role = model.AddNew();
        role.Name = "Registrar";
        role.Scopes = ["case:read"];

        Assert.Empty(model.Validate(["Developer"]));
    }

    // Scopes hidden for the team by ITeamScopeVisibilityProvider (Tharga/Team#334)

    private static readonly IReadOnlySet<string> AtlasHidden = new HashSet<string> { "atlas:read", "atlas:manage" };

    [Fact]
    public void A_hidden_scope_is_left_out_of_the_editor()
    {
        var model = new TenantRoleManagerModel();
        model.Load([new TenantRoleDefinition("Analyst", ["case:read", "atlas:manage"])], AtlasHidden);

        Assert.Equal(["case:read"], Assert.Single(model.Roles).Scopes);
    }

    /// <summary>Switching a feature off must not strip its scopes from the team's roles when one is edited.</summary>
    [Fact]
    public void Saving_keeps_a_hidden_scope_the_role_held()
    {
        var model = new TenantRoleManagerModel();
        model.Load([new TenantRoleDefinition("Analyst", ["case:read", "atlas:manage"])], AtlasHidden);

        var role = Assert.Single(model.Roles);
        role.Scopes = ["case:read", "case:write"];

        Assert.Equal(["case:read", "case:write", "atlas:manage"], Assert.Single(model.ToDefinitions()).Scopes);
    }

    [Fact]
    public void Removing_every_visible_scope_still_keeps_the_hidden_ones()
    {
        var model = new TenantRoleManagerModel();
        model.Load([new TenantRoleDefinition("Analyst", ["case:read", "atlas:manage"])], AtlasHidden);

        Assert.Single(model.Roles).Scopes = [];

        Assert.Equal(["atlas:manage"], Assert.Single(model.ToDefinitions()).Scopes);
    }

    [Fact]
    public void A_new_role_has_no_hidden_scopes_to_keep()
    {
        var model = new TenantRoleManagerModel();
        model.Load([], AtlasHidden);
        var added = model.AddNew();
        added.Name = "Reader";
        added.Scopes = ["case:read"];

        Assert.Equal(["case:read"], Assert.Single(model.ToDefinitions()).Scopes);
    }

    [Fact]
    public void Nothing_hidden_round_trips_unchanged()
    {
        var model = new TenantRoleManagerModel();
        model.Load([new TenantRoleDefinition("Analyst", ["case:read", "atlas:manage"])], hiddenScopes: null);

        Assert.Equal(["case:read", "atlas:manage"], Assert.Single(model.Roles).Scopes);
        Assert.Equal(["case:read", "atlas:manage"], Assert.Single(model.ToDefinitions()).Scopes);
    }
}
