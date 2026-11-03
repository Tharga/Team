using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Smoke tests for the <c>ScopeView</c> component parameters. Mirrors the reflection-based style of the
/// other component parameter tests (no bUnit in this project).
/// </summary>
public class ScopeViewTests
{
    [Theory]
    [InlineData("ShowDescription", true)]
    [InlineData("ShowRoles", true)]
    [InlineData("ShowAccessLevelSelector", true)]
    [InlineData("AllowGridSorting", true)]
    [InlineData("AllowGridFiltering", false)]
    [InlineData("ShowSystemScopes", true)]
    public void BoolParameter_HasExpectedDefault_AndParameterAttribute(string propertyName, bool expectedDefault)
    {
        var componentType = ResolveScopeView();
        var prop = componentType.GetProperty(propertyName);

        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop.PropertyType);
        Assert.NotNull(prop.GetCustomAttribute<ParameterAttribute>());

        var instance = Activator.CreateInstance(componentType);
        Assert.Equal(expectedDefault, (bool)prop.GetValue(instance));
    }

    [Fact]
    public void RoleLabel_IsAParameter_AndNullByDefault()
    {
        var componentType = ResolveScopeView();
        var prop = componentType.GetProperty("RoleLabel");

        Assert.NotNull(prop);
        Assert.Equal(typeof(Func<string, string>), prop.PropertyType);
        Assert.NotNull(prop.GetCustomAttribute<ParameterAttribute>());
        Assert.Null(prop.GetValue(Activator.CreateInstance(componentType)));
    }

    [Fact]
    public void RoleText_ShowsTheHostsLabel_AndFallsBackToTheIdentifier()
    {
        Func<string, string> label = name => name == "SourceViewer" ? "Källvisare" : null;

        Assert.Equal("Källvisare", Features.Scopes.ScopeReference.RoleText(label, "SourceViewer"));
        Assert.Equal("Editor", Features.Scopes.ScopeReference.RoleText(label, "Editor"));
        Assert.Equal("SourceViewer", Features.Scopes.ScopeReference.RoleText(null, "SourceViewer"));
        Assert.Equal("SourceViewer", Features.Scopes.ScopeReference.RoleText(_ => " ", "SourceViewer"));
    }

    /// <summary>
    /// The label is for display only: the member's assigned roles are identifiers, and pre-selection must keep matching them
    /// whatever the host shows. This is why a host cannot pass the label as the role's name.
    /// </summary>
    [Fact]
    public void PreselectedRoles_StillMatchOnTheIdentifier_WhateverTheLabel()
    {
        IReadOnlyList<TenantRoleDefinition> known = [new TenantRoleDefinition("SourceViewer", ["orders:read"])];

        var selected = Features.Scopes.ScopeReference.PreselectedRoles(["SourceViewer"], known);

        Assert.Equal(["SourceViewer"], selected);
        Assert.Equal("Källvisare", Features.Scopes.ScopeReference.RoleText(_ => "Källvisare", selected[0]));
    }

    private static Type ResolveScopeView()
    {
        var blazorAssembly = typeof(Tharga.Team.Blazor.Framework.ThargaBlazorRegistration).Assembly;
        return blazorAssembly
            .GetTypes()
            .Single(t => t.Namespace == "Tharga.Team.Blazor.Features.Scopes" && t.Name == "ScopeView");
    }
}
