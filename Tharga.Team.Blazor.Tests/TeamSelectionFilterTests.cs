using Tharga.Team.Blazor.Features.Team;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The team page's "show only the selected team" option (Tharga/Team#400): what it narrows to, and when it gives up.
/// </summary>
public class TeamSelectionFilterTests
{
    private static readonly string[] Teams = ["a", "b", "c"];
    private static string KeyOf(string team) => team;

    [Fact]
    public void OptionOff_ShowsEveryTeam_AndOffersNoSwitch()
    {
        Assert.Equal(Teams, TeamSelectionFilter.Apply(Teams, selectedOnly: false, showAll: false, "b", KeyOf));
        Assert.False(TeamSelectionFilter.ShowToggle(Teams, selectedOnly: false, "b", KeyOf));
    }

    [Fact]
    public void OptionOn_ShowsOnlyTheSelectedTeam_AndOffersTheSwitch()
    {
        Assert.Equal(["b"], TeamSelectionFilter.Apply(Teams, selectedOnly: true, showAll: false, "b", KeyOf));
        Assert.True(TeamSelectionFilter.ShowToggle(Teams, selectedOnly: true, "b", KeyOf));
    }

    [Fact]
    public void ShowAll_WidensTheListAgain_AndKeepsTheSwitch()
    {
        Assert.Equal(Teams, TeamSelectionFilter.Apply(Teams, selectedOnly: true, showAll: true, "b", KeyOf));
        Assert.True(TeamSelectionFilter.ShowToggle(Teams, selectedOnly: true, "b", KeyOf));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zzz")]
    public void NoSelectedTeamInTheList_ShowsEveryTeam_RatherThanAnEmptyPage(string selected)
    {
        Assert.Equal(Teams, TeamSelectionFilter.Apply(Teams, selectedOnly: true, showAll: false, selected, KeyOf));
        Assert.False(TeamSelectionFilter.IsNarrowed(Teams, selectedOnly: true, showAll: false, selected, KeyOf));
        Assert.False(TeamSelectionFilter.ShowToggle(Teams, selectedOnly: true, selected, KeyOf));
    }

    [Fact]
    public void ACallerWithOnlyTheSelectedTeam_HasNothingToSwitchTo()
    {
        Assert.Equal(["a"], TeamSelectionFilter.Apply(["a"], selectedOnly: true, showAll: false, "a", KeyOf));
        Assert.False(TeamSelectionFilter.ShowToggle(["a"], selectedOnly: true, "a", KeyOf));
    }

    [Fact]
    public void NullTeams_AreAnEmptyList()
    {
        Assert.Empty(TeamSelectionFilter.Apply<string>(null, selectedOnly: true, showAll: false, "a", KeyOf));
        Assert.False(TeamSelectionFilter.ShowToggle<string>(null, selectedOnly: true, "a", KeyOf));
    }
}
