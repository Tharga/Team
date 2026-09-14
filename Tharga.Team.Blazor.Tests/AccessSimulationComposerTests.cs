using Tharga.Team;
using Tharga.Team.Blazor.Features.Simulation;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The simulation dialog composes one target from an access level, roles and hand-ticked scopes.
/// </summary>
public class AccessSimulationComposerTests
{
    private static readonly IReadOnlyDictionary<AccessLevel, IReadOnlyList<string>> LevelScopes =
        new Dictionary<AccessLevel, IReadOnlyList<string>>
        {
            [AccessLevel.Owner] = ["orders:read", "orders:write", "billing:manage"],
            [AccessLevel.Viewer] = ["orders:read"]
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RoleScopes =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["Editor"] = ["orders:write", "content:publish"],
            ["Support"] = ["orders:read", "valuegroup:read"]
        };

    private static AccessSimulation Build(SimulationSelection selection)
        => AccessSimulationComposer.Build(selection, LevelScopes, RoleScopes);

    // --- inherited scopes ---

    [Fact]
    public void Inherited_IsTheLevelsScopes()
        => Assert.Equal(["orders:read"], AccessSimulationComposer.Inherited(AccessLevel.Viewer, [], LevelScopes, RoleScopes).Order());

    [Fact]
    public void Inherited_AddsEveryRolesScopes()
        => Assert.Equal(
            ["content:publish", "orders:read", "orders:write", "valuegroup:read"],
            AccessSimulationComposer.Inherited(AccessLevel.Viewer, ["Editor", "Support"], LevelScopes, RoleScopes).Order());

    [Fact]
    public void Inherited_IsEmpty_WithNoLevelAndNoRoles()
        => Assert.Empty(AccessSimulationComposer.Inherited(null, [], LevelScopes, RoleScopes));

    [Fact]
    public void Inherited_IgnoresARoleItDoesNotKnow()
        => Assert.Empty(AccessSimulationComposer.Inherited(null, ["Ghost"], LevelScopes, RoleScopes));

    // --- the target ---

    [Fact]
    public void TheTarget_IsLevelUnionRolesUnionTickedScopes()
    {
        var simulation = Build(new SimulationSelection(AccessLevel.Viewer, ["Editor"], ["reports:export"]));

        Assert.Equal(["content:publish", "orders:read", "orders:write", "reports:export"], simulation.Scopes.Order());
    }

    [Fact]
    public void TheTarget_CarriesTheChosenAccessLevel()
        => Assert.Equal(AccessLevel.Viewer, Build(new SimulationSelection(AccessLevel.Viewer, [], [])).AccessLevel);

    [Fact]
    public void TheTarget_CarriesNoAccessLevel_WhenNoneWasChosen()
        => Assert.Null(Build(new SimulationSelection(null, ["Editor"], [])).AccessLevel);

    /// <summary>Every kind drops system scopes and app roles — a simulation shows access within the team.</summary>
    [Fact]
    public void TheTarget_DropsSystemAccess()
    {
        var simulation = Build(new SimulationSelection(AccessLevel.Viewer, [], []));

        Assert.True(simulation.DropSystemScopes);
        Assert.True(simulation.DropAppRoles);
    }

    [Fact]
    public void ATickedScopeAlreadyInherited_IsNotCountedTwice()
        => Assert.Single(Build(new SimulationSelection(AccessLevel.Viewer, [], ["orders:read"])).Scopes);

    // --- kind and label ---

    [Fact]
    public void AnUneditedMemberPick_IsAUserSimulationNamedAfterThem()
    {
        var simulation = Build(new SimulationSelection(AccessLevel.Viewer, ["Editor"], [], MemberName: "Bob"));

        Assert.Equal(AccessSimulationKind.User, simulation.Kind);
        Assert.Equal("Bob", simulation.Label);
    }

    [Fact]
    public void AnythingElse_IsComposed()
        => Assert.Equal(AccessSimulationKind.Composed, Build(new SimulationSelection(AccessLevel.Viewer, [], [])).Kind);

    /// <summary>
    /// Built from names only — the level, the roles and any scopes ticked beyond what they grant. The label is
    /// recorded as <c>simulation.target</c> on audit entries, which must not depend on the operator's language.
    /// </summary>
    [Fact]
    public void TheComposedLabel_NamesTheLevelRolesAndExtraScopes()
        => Assert.Equal(
            "Viewer + Editor + Support + reports:export",
            Build(new SimulationSelection(AccessLevel.Viewer, ["Editor", "Support"], ["orders:read", "reports:export"])).Label);

    [Fact]
    public void TheComposedLabel_ForNothingChosen_SaysSo()
        => Assert.Equal("no scopes", Build(new SimulationSelection(null, [], [])).Label);
}
