using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Role-source selection for the API-key role picker (Tharga/Team#120): the per-team merged set
/// (code roles ∪ the team's custom roles) via <see cref="ITenantRoleService"/> when dynamic roles are
/// enabled, otherwise the code-registered roles from <see cref="ITenantRoleRegistry"/>.
/// </summary>
public class ApiKeyRolePickerTests
{
    [Fact]
    public async Task ResolveAsync_UsesMergedSet_WhenServiceAndTeamKeyPresent()
    {
        var service = new Mock<ITenantRoleService>();
        service.Setup(s => s.GetRolesAsync("T1"))
            .ReturnsAsync([new TenantRoleDefinition("Developer", ["system:read"]),
                           new TenantRoleDefinition("Registrar", ["case:read"])]);
        var registry = new TenantRoleRegistry();
        registry.Register("Developer", "system:read");

        var result = await ApiKeyRolePicker.ResolveAsync(service.Object, registry, "T1");

        Assert.Contains(result, r => r.Name == "Registrar");
        service.Verify(s => s.GetRolesAsync("T1"), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToRegistry_WhenServiceNull()
    {
        var registry = new TenantRoleRegistry();
        registry.Register("Developer", "system:read");

        var result = await ApiKeyRolePicker.ResolveAsync(null, registry, "T1");

        Assert.Equal("Developer", Assert.Single(result).Name);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToRegistry_WhenTeamKeyEmpty()
    {
        var service = new Mock<ITenantRoleService>();
        var registry = new TenantRoleRegistry();
        registry.Register("Developer", "system:read");

        var result = await ApiKeyRolePicker.ResolveAsync(service.Object, registry, null);

        Assert.Equal("Developer", Assert.Single(result).Name);
        service.Verify(s => s.GetRolesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_Empty_WhenNothingRegistered()
        => Assert.Empty(await ApiKeyRolePicker.ResolveAsync(null, null, "T1"));
}
