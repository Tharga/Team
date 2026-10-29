using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What the panel's layout rests on, pinned in its stylesheet and in the rules its script adds. bUnit renders markup,
/// not layout, so a header that pushes its buttons out of the panel (Tharga/Team#418), or a drawer that the hide rule
/// still hides (#419), passes every render test.
/// </summary>
public class SupportPanelStyleTests
{
    private const string HideRule = ".rz-layout .rz-sidebar.support-panel-sidebar { display: none; }";
    private const string DrawerRule = ".rz-layout .rz-sidebar.support-panel-sidebar.support-panel-sidebar--drawer { display: block;";

    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Singleline);

    [Fact]
    public void TheHeader_Wraps_SoEveryButtonStaysInThePanel()
    {
        Assert.Contains("flex-wrap: wrap", Rule(".support-panel__header"));
    }

    [Fact]
    public void TheActions_KeepToTheRight_WhenTheHeaderWraps()
    {
        Assert.Contains("margin-inline-start: auto", Rule(".support-panel__actions"));
    }

    /// <summary>A host that overrode the hide rule from its own stylesheet keeps working, because its override was written against this selector.</summary>
    [Fact]
    public void TheHideRule_KeepsItsSelector()
    {
        Assert.Contains(HideRule, Script());
    }

    /// <summary>The hide rule's selector and one class more, so a drawer is shown without <c>!important</c>.</summary>
    [Fact]
    public void TheDrawerRule_OutranksTheHideRule()
    {
        Assert.Contains(DrawerRule, Script());
    }

    private static string Script() => File.ReadAllText(ComponentPath("wwwroot/support-panel.js"));

    /// <summary>Every declaration a selector gets outside a media query, from each rule that lists it.</summary>
    private static string Rule(string selector)
    {
        var css = Comment.Replace(File.ReadAllText(ComponentPath("Features/Support/SupportPanel.razor.css")), string.Empty);
        return string.Join("\n", TopLevelRules(css).Where(x => x.Selectors.Contains(selector)).Select(x => x.Body));
    }

    private static IEnumerable<(string[] Selectors, string Body)> TopLevelRules(string css)
    {
        var depth = 0;
        var start = 0;
        var selector = string.Empty;

        for (var i = 0; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                if (depth == 0)
                {
                    selector = css[start..i].Trim();
                    start = i + 1;
                }

                depth++;
            }
            else if (css[i] == '}' && --depth == 0)
            {
                if (!selector.StartsWith('@')) yield return (selector.Split(',', StringSplitOptions.TrimEntries), css[start..i]);
                start = i + 1;
            }
        }
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
