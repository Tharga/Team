using System.Text.RegularExpressions;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The users, teams and directory-only grids say how many rows there are and how many pages (Tharga/Team#343),
/// in a text a host can translate.
/// </summary>
public class PagingSummaryTests
{
    public sealed record Grid(string Path, string Catalogue, string ExpectedDefault, string ExpectedKey)
    {
        public override string ToString() => Catalogue;
    }

    public static TheoryData<Grid> Grids() => new()
    {
        new Grid("Features/User/UsersListView.razor", nameof(UsersListViewText), "{2} users, page {0} of {1}", "team.usersList.pagingSummary"),
        new Grid("Features/User/TeamsListView.razor", nameof(TeamsListViewText), "{2} teams, page {0} of {1}", "team.teamsList.pagingSummary"),
        new Grid("Features/User/DirectoryOnlyUsersView.razor", nameof(DirectoryOnlyUsersViewText), "{2} users, page {0} of {1}", "team.directoryOnly.pagingSummary"),
    };

    private static TextKey KeyOf(string catalogue) => catalogue switch
    {
        nameof(UsersListViewText) => UsersListViewText.PagingSummary,
        nameof(TeamsListViewText) => TeamsListViewText.PagingSummary,
        nameof(DirectoryOnlyUsersViewText) => DirectoryOnlyUsersViewText.PagingSummary,
        _ => throw new ArgumentOutOfRangeException(nameof(catalogue))
    };

    private static TextKey[] AllOf(string catalogue) => catalogue switch
    {
        nameof(UsersListViewText) => UsersListViewText.All,
        nameof(TeamsListViewText) => TeamsListViewText.All,
        nameof(DirectoryOnlyUsersViewText) => DirectoryOnlyUsersViewText.All,
        _ => throw new ArgumentOutOfRangeException(nameof(catalogue))
    };

    private static string ComponentSource(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, "Tharga.Team.Blazor", relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void TheDefault_ReadsAsCountAndPage_WithRadzensPlaceholders(Grid grid)
    {
        var key = KeyOf(grid.Catalogue);

        Assert.Equal(grid.ExpectedDefault, key.Default);
        Assert.Equal(grid.ExpectedKey, key.Key);
        Assert.Contains("page 2 of 5", string.Format(key.Default, 2, 5, 42));
        Assert.StartsWith("42 ", string.Format(key.Default, 2, 5, 42));
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void TheKey_IsResolvedWithTheViewsOtherText_SoAHostCanTranslateIt(Grid grid)
    {
        Assert.Contains(KeyOf(grid.Catalogue), AllOf(grid.Catalogue));
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void ThePagedGrid_ShowsTheSummary_FromItsOwnCatalogue(Grid view)
    {
        var source = ComponentSource(view.Path);
        var grid = Regex.Match(source, @"<RadzenDataGrid\b[^>]*AllowPaging=""true""[^>]*>", RegexOptions.Singleline);
        var catalogue = view.Catalogue;

        Assert.True(grid.Success, $"{view.Path} has no paged RadzenDataGrid.");
        Assert.Contains("ShowPagingSummary=\"true\"", grid.Value);
        Assert.Contains($"PagingSummaryFormat=\"@_text[{catalogue}.PagingSummary]\"", grid.Value);
    }

    [Fact]
    public void AllThreeKeys_AreDistinct()
    {
        TextKey[] keys = [UsersListViewText.PagingSummary, TeamsListViewText.PagingSummary, DirectoryOnlyUsersViewText.PagingSummary];

        Assert.Equal(3, keys.Select(k => k.Key).Distinct().Count());
    }
}
