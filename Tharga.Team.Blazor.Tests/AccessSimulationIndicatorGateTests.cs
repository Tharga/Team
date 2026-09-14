using Tharga.Team.Blazor.Features.Simulation;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The compact top-bar indicator shows while access is reduced — and never during a demo.
/// </summary>
public class AccessSimulationIndicatorGateTests
{
    private static AccessSimulation Active(AccessSimulationKind kind) => new() { Kind = kind, Label = "x", TeamKey = "team-1" };

    [Theory]
    [InlineData(AccessSimulationKind.User)]
    [InlineData(AccessSimulationKind.Role)]
    [InlineData(AccessSimulationKind.Scopes)]
    [InlineData(AccessSimulationKind.AccessLevel)]
    [InlineData(AccessSimulationKind.Composed)]
    public void Shows_WhileASimulationIsActive(AccessSimulationKind kind)
        => Assert.True(AccessSimulationIndicatorGate.Show(enabled: true, Active(kind)));

    [Fact]
    public void Hides_WithNoSimulation()
        => Assert.False(AccessSimulationIndicatorGate.Show(enabled: true, active: null));

    /// <summary>
    /// A demo exists to look like an ordinary member's session. An indicator saying access is reduced would give
    /// it away, exactly as the banner would (Tharga/Team#223).
    /// </summary>
    [Fact]
    public void Hides_DuringDemoMode()
        => Assert.False(AccessSimulationIndicatorGate.Show(enabled: true, Active(AccessSimulationKind.Demo)));

    [Fact]
    public void Hides_WhenSimulationIsNotEnabled()
        => Assert.False(AccessSimulationIndicatorGate.Show(enabled: false, Active(AccessSimulationKind.User)));

    /// <summary>A kind added later is visible by default — hiding is the exception and has to be named.</summary>
    [Fact]
    public void EveryKindExceptDemo_Shows()
        => Assert.All(
            Enum.GetValues<AccessSimulationKind>().Where(k => k != AccessSimulationKind.Demo),
            kind => Assert.True(AccessSimulationIndicatorGate.Show(true, Active(kind))));
}
