using System.Reflection;
using Microsoft.AspNetCore.Components;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The two API key views let the person creating a key make it personal (Tharga/Team#314, 4.1), as far as
/// <see cref="ApiKeyOptions.TeamKeys"/> and <see cref="ApiKeyOptions.SystemKeys"/> allow.
/// </summary>
public class ApiKeyViewPersonalKeysTests
{
    /// <summary>
    /// <c>Mine</c>, not <c>None</c>. Before 4.1 the team view's default hid the option, so a page that set
    /// nothing could not create a personal key at all. <c>Mine</c> is the narrowest value that still lists the
    /// key the caller has just made.
    /// </summary>
    [Theory]
    [InlineData("ApiKeyView")]
    [InlineData("SystemApiKeyView")]
    public void ShowPrivateKeys_DefaultsToMine(string view)
    {
        var componentType = ResolveView(view);
        var prop = componentType.GetProperty("ShowPrivateKeys");

        Assert.NotNull(prop);
        Assert.NotNull(prop.GetCustomAttribute<ParameterAttribute>());
        Assert.Equal(PrivateKeyScope.Mine, (PrivateKeyScope)prop.GetValue(Activator.CreateInstance(componentType)));
    }

    /// <summary>A checkbox, as asked for, labelled "Personal", with no "Private" heading above it.</summary>
    [Theory]
    [InlineData("ApiKeyView")]
    [InlineData("SystemApiKeyView")]
    public void ThePersonalOption_IsACheckboxLabelledPersonal(string view)
    {
        var markup = File.ReadAllText(ViewPath(view));

        Assert.Contains("<RadzenCheckBox TValue=\"bool\" @bind-Value=\"_newPersonal\"", markup);
        Assert.Contains("<RadzenLabel Text=\"@_text[ApiKeyViewText.Personal]\"", markup);
        Assert.DoesNotContain("RadzenSwitch", markup);
        Assert.DoesNotContain("Text=\"Private\"", markup);
        Assert.DoesNotContain("only me", markup);
    }

    [Fact]
    public void TheWording_IsPersonal()
    {
        Assert.Equal("Personal", ApiKeyViewText.Personal.Default);
        Assert.DoesNotContain(ApiKeyViewText.All, k => k.Default.Contains("Private", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ApiKeyOwnership.Both, PrivateKeyScope.Mine, true)]
    [InlineData(ApiKeyOwnership.Both, PrivateKeyScope.All, true)]
    [InlineData(ApiKeyOwnership.Both, PrivateKeyScope.None, false)]
    [InlineData(ApiKeyOwnership.Shared, PrivateKeyScope.Mine, false)]
    [InlineData(ApiKeyOwnership.Personal, PrivateKeyScope.Mine, false)]
    public void TheCheckbox_ShowsOnlyWhenThereIsAChoiceAndTheKeyWouldBeListed(ApiKeyOwnership ownership, PrivateKeyScope shown, bool expected)
    {
        Assert.Equal(expected, PersonalKeyChoice.ShowCheckbox(ownership, shown));
    }

    /// <summary>When every key is personal, a page left at <c>None</c> still lists the caller's own, or it could never see a key it creates.</summary>
    [Theory]
    [InlineData(ApiKeyOwnership.Personal, PrivateKeyScope.None, PrivateKeyScope.Mine)]
    [InlineData(ApiKeyOwnership.Personal, PrivateKeyScope.All, PrivateKeyScope.All)]
    [InlineData(ApiKeyOwnership.Both, PrivateKeyScope.None, PrivateKeyScope.None)]
    [InlineData(ApiKeyOwnership.Shared, PrivateKeyScope.Mine, PrivateKeyScope.Mine)]
    public void TheListedKeys_FollowThePage_ExceptWhenEveryKeyIsPersonal(ApiKeyOwnership ownership, PrivateKeyScope shown, PrivateKeyScope expected)
    {
        Assert.Equal(expected, PersonalKeyChoice.Listed(ownership, shown));
    }

    [Theory]
    [InlineData(ApiKeyOwnership.Both, true, true)]
    [InlineData(ApiKeyOwnership.Both, false, false)]
    [InlineData(ApiKeyOwnership.Shared, true, false)]
    [InlineData(ApiKeyOwnership.Personal, false, true)]
    public void ANewKey_IsPersonalWhenTickedOrRequired(ApiKeyOwnership ownership, bool ticked, bool expected)
    {
        Assert.Equal(expected, PersonalKeyChoice.IsPersonal(ownership, ticked));
    }

    private static Type ResolveView(string name)
        => typeof(ThargaBlazorRegistration).Assembly
            .GetTypes()
            .Single(t => t.Namespace == "Tharga.Team.Blazor.Features.Api" && t.Name == name);

    private static string ViewPath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor", "Features", "Api", $"{name}.razor");
    }
}
