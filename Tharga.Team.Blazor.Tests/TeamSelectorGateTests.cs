using Tharga.Team.Blazor.Features.Team;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Tests for <see cref="TeamSelectorGate"/> — what the team selector offers a caller who belongs to no
/// team. Pure-function tests to match the other gating tests in this project (no bUnit, so razor markup
/// cannot be asserted directly).
/// </summary>
public class TeamSelectorGateTests
{
    /// <summary>The ordinary case the link exists for: a new user with no teams and creation allowed.</summary>
    [Fact]
    public void ShowCreateTeamLink_NoTeamsAndCreationAllowed_IsShown()
    {
        Assert.True(TeamSelectorGate.ShowCreateTeamLink(0, true));
    }

    /// <summary>
    /// The defect this closes. A host setting <c>AllowTeamCreation = false</c> still saw a "Create team"
    /// link, and following it reached an operation the service layer refuses — team creation has required
    /// the option at the service since 3.1.2.
    /// </summary>
    [Fact]
    public void ShowCreateTeamLink_CreationDisabled_IsHidden()
    {
        Assert.False(TeamSelectorGate.ShowCreateTeamLink(0, false));
    }

    /// <summary>
    /// The link belongs to the teamless branch only. A caller who already has teams gets the selector
    /// itself, so creation must not appear here regardless of the option.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(5, true)]
    [InlineData(5, false)]
    public void ShowCreateTeamLink_CallerHasTeams_IsHidden(int teamCount, bool allowTeamCreation)
    {
        Assert.False(TeamSelectorGate.ShowCreateTeamLink(teamCount, allowTeamCreation));
    }

    /// <summary>
    /// Agrees with <c>TeamComponent</c>, which has always read the option. The two surfaces contradicting
    /// each other is what made this a defect rather than a missing feature, so the agreement is the thing
    /// worth pinning.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShowCreateTeamLink_MatchesTeamComponentForATeamlessCaller(bool allowTeamCreation)
    {
        // TeamComponent renders its "Create new Team" button on `!_teams.Any() && _allowTeamCreation`.
        var teamComponentShowsButton = allowTeamCreation;

        Assert.Equal(teamComponentShowsButton, TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation));
    }

    // --- ShowCreateTeamLink: hiding the top-bar link without touching what governs creation ---

    /// <summary>
    /// The ask: creation stays reachable from the team page, but the top bar does not advertise it.
    /// Purely a layout decision, so it is the selector's own parameter rather than the shared option.
    /// </summary>
    [Fact]
    public void ShowCreateTeamLink_HiddenByTheParameter_EvenWhenCreationIsAllowed()
    {
        Assert.True(TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation: true, showLink: true));
        Assert.False(TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation: true, showLink: false));
    }

    /// <summary>
    /// <b>Hiding the link is not a permission.</b> The parameter suppresses an affordance; whether teams
    /// may be created is <c>AllowTeamCreation</c>, which the service enforces too. Asserted so nobody
    /// later "simplifies" the two into one flag — that would turn a layout switch into the only thing
    /// standing between a caller and an operation, which is the shape of a security bug rather than a
    /// tidy-up.
    /// </summary>
    [Fact]
    public void ShowCreateTeamLink_TheParameterCannotSubstituteForTheOption()
    {
        // Creation disabled: the link is gone whatever the parameter says.
        Assert.False(TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation: false, showLink: true));
        Assert.False(TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation: false, showLink: false));
    }

    /// <summary>Default true, so an existing host sees no change.</summary>
    [Fact]
    public void ShowCreateTeamLink_DefaultsToShown()
    {
        Assert.Equal(
            TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation: true),
            TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation: true, showLink: true));
    }

    /// <summary>
    /// The self-check: without it, every assertion above would still pass if the parameter were ignored
    /// and the result driven entirely by the other two inputs.
    /// </summary>
    [Fact]
    public void ShowCreateTeamLink_TheParameterIsWhatDecides()
    {
        // Same teamCount, same option — only the parameter differs, and it must change the answer.
        Assert.NotEqual(
            TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation: true, showLink: true),
            TeamSelectorGate.ShowCreateTeamLink(0, allowTeamCreation: true, showLink: false));
    }

    // --- ShowSelectedTeamName / ShowPicker: what the selector draws once there are teams to draw ---

    /// <summary>
    /// The defect Tharga/Team#214 reported. A caller holding <c>teams:read</c> who belongs to no team sees
    /// every team and has selected none, and the top bar rendered <b>nothing at all</b> — not a disabled
    /// control, not an empty dropdown, not the "Create team" link. The picker must appear so the widened
    /// set is offered rather than unreachable.
    /// </summary>
    [Fact]
    public void ShowPicker_TeamsVisibleButNoneSelected_IsShown()
    {
        Assert.True(TeamSelectorGate.ShowPicker(5, hasSelection: false));
    }

    /// <summary>
    /// Wider than the issue states. It names the several-teams case, but a single visible team with no
    /// selection fell through the same gap for the same reason, so it is fixed by the same branch.
    /// </summary>
    [Fact]
    public void ShowPicker_OneTeamVisibleAndNoneSelected_IsShown()
    {
        Assert.True(TeamSelectorGate.ShowPicker(1, hasSelection: false));
        Assert.False(TeamSelectorGate.ShowSelectedTeamName(1, hasSelection: false));
    }

    /// <summary>
    /// Unchanged behaviour for the ordinary member: one team, already selected, so a dropdown over it
    /// could not do anything and the name is what is shown.
    /// </summary>
    [Fact]
    public void ShowSelectedTeamName_OneTeamSelected_IsTheNameNotThePicker()
    {
        Assert.True(TeamSelectorGate.ShowSelectedTeamName(1, hasSelection: true));
        Assert.False(TeamSelectorGate.ShowPicker(1, hasSelection: true));
    }

    /// <summary>Unchanged behaviour: several teams with one selected is the dropdown, as before.</summary>
    [Fact]
    public void ShowPicker_SeveralTeamsWithASelection_IsShown()
    {
        Assert.True(TeamSelectorGate.ShowPicker(4, hasSelection: true));
        Assert.False(TeamSelectorGate.ShowSelectedTeamName(4, hasSelection: true));
    }

    /// <summary>
    /// Neither branch claims the teamless caller — that is <see cref="TeamSelectorGate.ShowCreateTeamLink"/>'s
    /// state, and a picker over zero teams would be the same empty control the fix exists to avoid.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NeitherBranch_ClaimsTheTeamlessCaller(bool hasSelection)
    {
        Assert.False(TeamSelectorGate.ShowPicker(0, hasSelection));
        Assert.False(TeamSelectorGate.ShowSelectedTeamName(0, hasSelection));
    }

    /// <summary>
    /// <b>The actual defect, stated as a property.</b> #214 was not a wrong branch but a missing one: three
    /// individually correct rules met in a state none of them covered, and the component drew nothing. So
    /// what is worth pinning is exhaustiveness rather than any single case — every state with at least one
    /// visible team must be claimed by exactly one branch, whether or not anyone anticipated it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(50)]
    public void WithTeamsVisible_ExactlyOneBranchRenders(int teamCount)
    {
        foreach (var hasSelection in new[] { true, false })
        {
            var branches = new[]
            {
                TeamSelectorGate.ShowSelectedTeamName(teamCount, hasSelection),
                TeamSelectorGate.ShowPicker(teamCount, hasSelection)
            };

            Assert.True(branches.Count(x => x) == 1,
                $"{teamCount} team(s), selection={hasSelection} is claimed by {branches.Count(x => x)} branches. " +
                "Zero is Tharga/Team#214 — an empty top bar with no way to reach a team; two would render both.");
        }
    }

    /// <summary>
    /// The self-check for the exhaustiveness test above: it would pass just as happily against a pair of
    /// predicates that ignored their arguments and returned a constant true and a constant false, so this
    /// proves both inputs still reach both branches.
    /// </summary>
    [Fact]
    public void TheBranchesActuallyDependOnBothInputs()
    {
        // The selection is what moves a single team between the two branches.
        Assert.NotEqual(
            TeamSelectorGate.ShowPicker(1, hasSelection: true),
            TeamSelectorGate.ShowPicker(1, hasSelection: false));

        // And the count is what decides whether a selection means "nothing to choose" at all.
        Assert.NotEqual(
            TeamSelectorGate.ShowSelectedTeamName(1, hasSelection: true),
            TeamSelectorGate.ShowSelectedTeamName(2, hasSelection: true));
    }
}
