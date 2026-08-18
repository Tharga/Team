namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Whether an administrator may view the application as a less privileged user.
/// </summary>
public class AccessSimulationOptions
{
    /// <summary>
    /// Turns the feature on. Off by default.
    /// </summary>
    /// <remarks>
    /// Opt-in because it adds a visible control and a session cookie to every page for the callers who
    /// hold <see cref="SimulationScopes.Simulate"/>, and a host that does not want it should not have to
    /// hide anything. Nothing changes for a host that leaves this alone: the cookie is never read, the
    /// filter is never reached, and the scope is still registered but grants nothing anyone can use.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether <c>AccessSimulationBar</c> offers its entry point and banner. <c>true</c> by default, which
    /// is the behaviour hosts have today.
    /// </summary>
    /// <remarks>
    /// The default for a host that places the bar and says nothing else. The component's own
    /// <c>ShowEntryPoint</c> and <c>ShowBanner</c> parameters still win where they are set, so placing the
    /// bar by hand keeps full control.
    /// <para>
    /// <b>This does not govern demo mode.</b> A demo shows nothing in the navigation bar regardless of this
    /// setting — see <see cref="AccessSimulationKind.Demo"/>. What this switches off is the run-as banner and
    /// the entry point.
    /// </para>
    /// </remarks>
    public bool ShowInNavigation { get; set; } = true;
}
