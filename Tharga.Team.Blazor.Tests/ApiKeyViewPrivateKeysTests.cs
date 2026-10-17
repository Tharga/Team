using System.Reflection;
using Microsoft.AspNetCore.Components;
using Tharga.Team;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A default <c>ApiKeyView</c> lets the creator make a key private (Tharga/Team#314, 4.1).
/// </summary>
public class ApiKeyViewPrivateKeysTests
{
    /// <summary>
    /// <c>Mine</c>, not <c>None</c>. Before 4.1 the default hid the Private option, so a page that set nothing
    /// could not create a private key at all. <c>Mine</c> is the narrowest value that still lists the key the
    /// caller has just made.
    /// </summary>
    [Fact]
    public void ShowPrivateKeys_DefaultsToMine()
    {
        var componentType = ResolveApiKeyView();
        var prop = componentType.GetProperty("ShowPrivateKeys");

        Assert.NotNull(prop);
        Assert.NotNull(prop.GetCustomAttribute<ParameterAttribute>());
        Assert.Equal(PrivateKeyScope.Mine, (PrivateKeyScope)prop.GetValue(Activator.CreateInstance(componentType)));
    }

    /// <summary>The option is a checkbox, as asked for, rather than a switch.</summary>
    [Fact]
    public void ThePrivateOption_IsACheckbox()
    {
        var markup = File.ReadAllText(ApiKeyViewPath());

        Assert.Contains("<RadzenCheckBox TValue=\"bool\" @bind-Value=\"_newPrivate\"", markup);
        Assert.DoesNotContain("RadzenSwitch", markup);
    }

    private static Type ResolveApiKeyView()
        => typeof(Tharga.Team.Blazor.Framework.ThargaBlazorRegistration).Assembly
            .GetTypes()
            .Single(t => t.Namespace == "Tharga.Team.Blazor.Features.Api" && t.Name == "ApiKeyView");

    private static string ApiKeyViewPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor", "Features", "Api", "ApiKeyView.razor");
    }
}
