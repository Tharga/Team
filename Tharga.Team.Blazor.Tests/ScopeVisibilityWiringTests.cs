namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every component that lists a team's scopes asks the team's <see cref="ITeamScopeVisibilityProvider"/>
/// which to leave out (Tharga/Team#332, #334). No bUnit in this project, so the wiring is pinned in the
/// markup, the way <c>RoleScopePickerFilteringTests</c> pins the pickers' filtering.
/// </summary>
public class ScopeVisibilityWiringTests
{
    [Theory]
    [InlineData("Features/Team/TeamComponent.razor")]
    [InlineData("Features/Api/ApiKeyView.razor")]
    [InlineData("Features/Roles/TenantRoleManager.razor")]
    [InlineData("Features/Scopes/ScopeView.razor")]
    public void TheComponent_AsksTheProviderWhichScopesToHide(string relativePath)
    {
        var markup = File.ReadAllText(ComponentPath(relativePath));

        Assert.Contains("ITeamScopeVisibilityProvider", markup);
        Assert.Contains("ScopeVisibility.HiddenAsync(", markup);
    }

    /// <summary>A role's hidden scopes are kept when the team edits it, so the manager loads them through the overload that knows them.</summary>
    [Fact]
    public void TheRoleManager_LoadsRolesWithTheHiddenScopes()
    {
        var markup = File.ReadAllText(ComponentPath("Features/Roles/TenantRoleManager.razor"));

        Assert.Contains("_model.Load(roles, _hiddenScopes)", markup);
    }

    private static string ComponentPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor", relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
