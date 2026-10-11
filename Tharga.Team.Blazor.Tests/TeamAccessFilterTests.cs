using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The team page's "hide teams I have no access to" switch, and the short access tags in the team selector.
/// </summary>
public class TeamAccessFilterTests
{
    private static readonly (string Key, AccessLevel? Level)[] Teams =
    [
        ("a", AccessLevel.Owner),
        ("b", null),
        ("c", AccessLevel.Viewer)
    ];

    private static AccessLevel? LevelOf((string Key, AccessLevel? Level) team) => team.Level;

    [Fact]
    public void ShowToggle_WhenSomeTeamGivesNoAccess_IsTrue()
    {
        Assert.True(TeamAccessFilter.ShowToggle(Teams, LevelOf));
    }

    [Fact]
    public void ShowToggle_WhenEveryTeamIsReachable_IsFalse()
    {
        Assert.False(TeamAccessFilter.ShowToggle(Teams.Where(t => t.Level != null), LevelOf));
    }

    [Fact]
    public void Apply_Hiding_LeavesOnlyReachableTeams()
    {
        Assert.Equal(["a", "c"], TeamAccessFilter.Apply(Teams, true, LevelOf).Select(t => t.Key));
    }

    [Fact]
    public void Apply_NotHiding_LeavesEveryTeam()
    {
        Assert.Equal(["a", "b", "c"], TeamAccessFilter.Apply(Teams, false, LevelOf).Select(t => t.Key));
    }

    [Fact]
    public void Apply_NullList_IsEmpty()
    {
        Assert.Empty(TeamAccessFilter.Apply<(string, AccessLevel?)>(null, true, LevelOf));
    }

    [Theory]
    [InlineData(AccessLevel.Administrator, "Admin")]
    [InlineData(AccessLevel.Owner, "Owner")]
    [InlineData(AccessLevel.Viewer, "Viewer")]
    [InlineData(null, "No access")]
    public void ShortTag_ShortensOnlyWhatIsTooLong(AccessLevel? level, string expected)
    {
        Assert.Equal(expected, TextSet.Empty[AccessLevelText.ShortFor(level)]);
    }
}
