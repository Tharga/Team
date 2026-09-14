using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The Roles picker is offered only when there is a role to pick (Tharga/Team#275), in both the API-key view
/// and the member grid.
/// </summary>
public class RolePickerGateTests
{
    private static readonly TenantRoleDefinition Developer = new("Developer", ["system:read"]);

    [Fact]
    public void ShowRoles_True_WhenEnabledAndRolesExist()
        => Assert.True(RolePickerGate.ShowRoles(true, [Developer]));

    [Fact]
    public void ShowRoles_False_WhenEnabledButNoRolesExist()
        => Assert.False(RolePickerGate.ShowRoles(true, []));

    [Fact]
    public void ShowRoles_False_WhenRoleSetIsNull()
        => Assert.False(RolePickerGate.ShowRoles(true, null));

    [Fact]
    public void ShowRoles_False_WhenHostHasNotEnabledIt_EvenWithRoles()
        => Assert.False(RolePickerGate.ShowRoles(false, [Developer]));
}
