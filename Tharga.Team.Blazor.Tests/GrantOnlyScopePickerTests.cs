using System.Text.RegularExpressions;
using Match = System.Text.RegularExpressions.Match;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Guards the UI half of grant-only scopes (<see href="https://github.com/Tharga/Team/issues/232">Tharga/Team#232</see>):
/// no picker may offer a grant-only scope, because every picker that grants one is reachable by a team
/// administrator.
/// </summary>
/// <remarks>
/// Excluding a grant-only scope from <c>GetScopesForAccessLevel</c> alone would not protect it. Defining a
/// tenant custom role is authorized by <c>DynamicTenantRoleOptions.ManageScope</c> (<c>team:manage</c> by
/// default) and editing per-member overrides by <c>team:member:manage</c> — both held by every
/// administrator — so an unguarded picker is a self-grant route straight back in.
/// </remarks>
public class GrantOnlyScopePickerTests : BunitContext
{
    private const string GrantOnlyScope = "case:read";
    private const string OrdinaryScope = "doc:read";

    [Fact]
    public void Editor_Offers_The_Grantable_Scopes()
    {
        var cut = RenderEditor(allScopes: [OrdinaryScope], inherited: [], overrides: []);

        Assert.Equal([OrdinaryScope], OptionNames(cut));
    }

    [Fact]
    public void Editor_Does_Not_Offer_A_GrantOnly_Scope_Nobody_Holds()
    {
        // AllScopes is what the component feeds in, already filtered. The scope simply must not appear.
        var cut = RenderEditor(allScopes: [OrdinaryScope], inherited: [], overrides: []);

        Assert.DoesNotContain(GrantOnlyScope, OptionNames(cut));
    }

    [Fact]
    public void Editor_Shows_An_Inherited_GrantOnly_Scope_So_The_Effective_Set_Stays_Truthful()
    {
        var cut = RenderEditor(allScopes: [OrdinaryScope], inherited: [GrantOnlyScope], overrides: []);

        var option = Assert.Single(Options(cut), o => o.Name == GrantOnlyScope);
        Assert.True(option.Inherited, "An inherited grant-only scope must render disabled, not as a grantable option.");
    }

    [Fact]
    public void Editor_Shows_An_Existing_GrantOnly_Override_So_It_Can_Be_Removed()
    {
        var cut = RenderEditor(allScopes: [OrdinaryScope], inherited: [], overrides: [GrantOnlyScope]);

        var option = Assert.Single(Options(cut), o => o.Name == GrantOnlyScope);
        Assert.False(option.Inherited, "An existing override must stay removable — removal is de-escalation.");
    }

    [Fact]
    public void Editor_Does_Not_Duplicate_A_Scope_That_Is_Both_Offered_And_Inherited()
    {
        var cut = RenderEditor(allScopes: [OrdinaryScope], inherited: [OrdinaryScope], overrides: []);

        Assert.Single(Options(cut), o => o.Name == OrdinaryScope);
    }

    /// <summary>
    /// The picker feeds themselves. <c>ScopeOverrideEditor</c> cannot filter grant-only scopes on its own —
    /// it receives names, not definitions — so the exclusion lives at each call site, and a new call site
    /// that forgets it is the way this protection would be lost.
    /// </summary>
    [Fact]
    public void Every_Registry_Fed_Scope_Picker_Excludes_GrantOnly()
    {
        var sources = new[]
        {
            "Features/Team/TeamComponent.razor",
            "Features/Api/ApiKeyView.razor",
            "Features/Roles/TenantRoleManager.razor",
        };

        var checkedAssignments = 0;

        foreach (var relative in sources)
        {
            var path = ResolveSourcePath(relative);
            var source = File.ReadAllText(path);
            var assignments = Regex.Matches(source, @"_allScopeNames\s*=\s*_scopeRegistry[^;]*;");

            Assert.True(assignments.Count > 0,
                $"{relative} no longer assigns _allScopeNames from _scopeRegistry — this scan has gone blind and needs updating.");

            foreach (Match assignment in assignments)
            {
                Assert.True(assignment.Value.Contains("!s.GrantOnly"),
                    $"{relative} feeds a scope picker from the registry without excluding grant-only scopes: {assignment.Value.Trim()}");
                checkedAssignments++;
            }
        }

        Assert.Equal(sources.Length, checkedAssignments);
    }

    private IRenderedComponent<ScopeOverrideEditor> RenderEditor(string[] allScopes, string[] inherited, string[] overrides)
    {
        // Radzen's dropdown calls into JS on first render; nothing here asserts on that.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Mock.Of<IThargaTextProvider>(p =>
            p.GetAsync(It.IsAny<TextKey>()) == Task.FromResult(string.Empty)));

        return Render<ScopeOverrideEditor>(p => p
            .Add(x => x.AllScopes, allScopes)
            .Add(x => x.InheritedScopes, inherited)
            .Add(x => x.Overrides, overrides));
    }

    private static IReadOnlyList<ScopeOverrideEditor.ScopeOption> Options(IRenderedComponent<ScopeOverrideEditor> cut)
    {
        var field = typeof(ScopeOverrideEditor).GetField("_options",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (List<ScopeOverrideEditor.ScopeOption>)field.GetValue(cut.Instance);
    }

    private static string[] OptionNames(IRenderedComponent<ScopeOverrideEditor> cut)
        => Options(cut).Select(o => o.Name).ToArray();

    private static string ResolveSourcePath(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Tharga.Team.Blazor")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "Tharga.Team.Blazor", relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Source file not found: {path}");
        return path;
    }
}
