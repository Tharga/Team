using System.Reflection;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every text catalogue lists all its keys in <c>All</c>, because that is what a host enumerates to check its translations:
/// a key a catalogue declares and leaves out of its list is a string the host never learns to translate.
/// </summary>
public class TextCatalogueAllTests
{
    public static TheoryData<string> Catalogues()
    {
        var data = new TheoryData<string>();
        foreach (var type in CatalogueTypes()) data.Add(type.FullName);
        return data;
    }

    [Fact]
    public void TheCataloguesAreFound()
    {
        var names = CatalogueTypes().Select(x => x.Name).ToArray();

        Assert.Contains(nameof(TeamMenuText), names);
        Assert.Contains(nameof(UserMailText), names);
        Assert.True(names.Length > 30, $"Only {names.Length} catalogues were found, so the scan is looking in the wrong place.");
    }

    [Theory]
    [MemberData(nameof(Catalogues))]
    public void ACatalogue_ListsEveryKeyItDeclares_InAll(string catalogue)
    {
        var type = typeof(ThargaTextKeys).Assembly.GetType(catalogue)!;
        var declared = KeysOf(type);

        var all = AllOf(type);

        Assert.True(all != null, $"{type.Name} declares {declared.Length} text keys and has no public static All listing them.");
        Assert.All(declared, key => Assert.True(all.Any(x => x.Key == key.Key), $"{type.Name}.All leaves out '{key.Key}'."));
    }

    [Fact]
    public void TheProfileMenusKeys_AreListed()
    {
        Assert.Equal(["team.menu.user", "team.menu.team", "team.menu.logout", "team.menu.login", "team.menu.createTeam", "team.menu.loading"], TeamMenuText.All.Select(x => x.Key));
    }

    private static IEnumerable<Type> CatalogueTypes()
        => typeof(ThargaTextKeys).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true, IsPublic: true } && t.Name.EndsWith("Text", StringComparison.Ordinal))
            .Where(t => KeysOf(t).Length > 0)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static TextKey[] KeysOf(Type type)
        => [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsInitOnly && f.FieldType == typeof(TextKey))
            .Select(f => (TextKey)f.GetValue(null))];

    private static IReadOnlyList<TextKey> AllOf(Type type)
    {
        var field = type.GetField("All", BindingFlags.Public | BindingFlags.Static);
        if (field != null) return field.GetValue(null) as IEnumerable<TextKey> is { } fromField ? [.. fromField] : null;

        var property = type.GetProperty("All", BindingFlags.Public | BindingFlags.Static);
        return property?.GetValue(null) is IEnumerable<TextKey> fromProperty ? [.. fromProperty] : null;
    }
}
