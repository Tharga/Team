namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every role and scope multi-select can be searched, case-insensitively (Tharga/Team#275).
/// </summary>
/// <remarks>
/// A host registering a dozen features has a scope per action, so these lists get long, and without a filter
/// picking one means scrolling. Radzen's drop-down filter is case-sensitive unless told otherwise, so
/// <c>AllowFiltering</c> alone would not find <c>team:manage</c> from "Team". Filtering lives in markup, where
/// nothing compiled can be reflected over, so the guard reads the source.
/// </remarks>
public class RoleScopePickerFilteringTests
{
    private const string AllowFiltering = "AllowFiltering=\"true\"";
    private const string CaseInsensitive = "FilterCaseSensitivity=\"FilterCaseSensitivity.CaseInsensitive\"";

    [Fact]
    public void EveryRoleOrScopeMultiSelect_IsSearchableCaseInsensitively()
    {
        var offenders = RoleOrScopePickers()
            .Where(p => !IsSearchable(p.Tag))
            .Select(p => $"{p.File}:{p.Line}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"A role or scope multi-select must set {AllowFiltering} and {CaseInsensitive}. " +
            $"Missing at: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// A source scan that matches nothing passes forever while reading as "everything checked". This pins that
    /// the scan actually reaches the pickers it exists to guard.
    /// </summary>
    [Theory]
    [InlineData("RoleEditor.razor")]
    [InlineData("ScopeOverrideEditor.razor")]
    [InlineData("SystemApiKeyView.razor")]
    [InlineData("TenantRoleManager.razor")]
    [InlineData("AccessSimulationDialog.razor")]
    public void TheScan_FindsTheKnownPicker(string fileName)
    {
        Assert.Contains(RoleOrScopePickers(), p => Path.GetFileName(p.File) == fileName);
    }

    [Fact]
    public void TheScan_CatchesAPickerWithoutFiltering()
    {
        const string source = """
            <RadzenDropDown TValue="IEnumerable<string>"
                            Multiple="true"
                            Data="@_allScopes" />
            """;

        var tag = Assert.Single(DropDownTags(source));
        Assert.True(IsRoleOrScopeMultiSelect(tag.Tag));
        Assert.False(IsSearchable(tag.Tag));
    }

    [Fact]
    public void TheScan_CatchesFilteringThatIsCaseSensitive()
    {
        const string source = """
            <RadzenDropDown TValue="IEnumerable<string>" Multiple="true" AllowFiltering="true" Data="@_roles" />
            """;

        Assert.False(IsSearchable(Assert.Single(DropDownTags(source)).Tag));
    }

    /// <summary>
    /// <c>TValue="IEnumerable&lt;string&gt;"</c> puts a <c>&gt;</c> inside an attribute value, so a scanner
    /// that ends the tag at the first one never sees the attributes after it.
    /// </summary>
    [Fact]
    public void TheScan_ReadsPastAngleBracketsInsideAttributeValues()
    {
        const string source = """
            <RadzenDropDown TValue="IEnumerable<string>" Multiple="true" Data="@_scopes"
                            AllowFiltering="true" FilterCaseSensitivity="FilterCaseSensitivity.CaseInsensitive" />
            """;

        Assert.True(IsSearchable(Assert.Single(DropDownTags(source)).Tag));
    }

    [Fact]
    public void TheScan_IgnoresSingleSelectsAndUnrelatedLists()
    {
        const string source = """
            <RadzenDropDown TValue="AccessLevel" Data="@_accessLevels" />
            <RadzenDropDown TValue="IEnumerable<string>" Multiple="true" Data="@_teams" />
            """;

        Assert.DoesNotContain(DropDownTags(source), t => IsRoleOrScopeMultiSelect(t.Tag));
    }

    private static bool IsSearchable(string tag)
        => tag.Contains(AllowFiltering, StringComparison.Ordinal) && tag.Contains(CaseInsensitive, StringComparison.Ordinal);

    private static bool IsRoleOrScopeMultiSelect(string tag)
        => tag.Contains("Multiple=\"true\"", StringComparison.Ordinal)
           && (tag.Contains("role", StringComparison.OrdinalIgnoreCase) || tag.Contains("scope", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string File, int Line, string Tag)> RoleOrScopePickers()
        => Directory.GetFiles(ComponentRoot(), "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => DropDownTags(File.ReadAllText(file))
                .Where(t => IsRoleOrScopeMultiSelect(t.Tag))
                .Select(t => (Path.GetRelativePath(ComponentRoot(), file), t.Line, t.Tag)));

    private static IEnumerable<(int Line, string Tag)> DropDownTags(string source)
    {
        const string open = "<RadzenDropDown ";
        var start = source.IndexOf(open, StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = EndOfTag(source, start);
            var line = source.AsSpan(0, start).Count('\n') + 1;
            yield return (line, source[start..end]);
            start = source.IndexOf(open, end, StringComparison.Ordinal);
        }
    }

    private static int EndOfTag(string source, int start)
    {
        var inQuotes = false;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '"') inQuotes = !inQuotes;
            else if (source[i] == '>' && !inQuotes) return i + 1;
        }

        return source.Length;
    }

    private static string ComponentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor");
    }
}
