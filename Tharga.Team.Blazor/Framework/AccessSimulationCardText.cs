namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>AccessSimulationCard</c> — the profile-page entry point for
/// impersonation and demo mode.
/// </summary>
/// <remarks>
/// Separate from the wording in <c>AccessSimulationBar</c>: the bar is a warning banner shown on every
/// page while a simulation is active, the card is a deliberate control a host places on a profile page.
/// They say different things to different readers, so sharing keys would force one phrasing to serve both.
/// <para>
/// Note that <c>AccessSimulationTargets.DemoLabel</c> is <b>not</b> here and is deliberately not
/// localizable — it is written to audit metadata, where a value that varies by operator language cannot be
/// searched or compared. <see cref="DemoMode"/> is the visible wording; the label is the record.
/// </para>
/// </remarks>
public static class AccessSimulationCardText
{
    public static readonly TextKey Title = new("team.simulation.card.title", "Access");

    public static readonly TextKey Expand = new("team.simulation.card.expand", "Show access options");

    public static readonly TextKey Collapse = new("team.simulation.card.collapse", "Hide access options");

    public static readonly TextKey DemoMode = new("team.simulation.card.demoMode", "Demo mode");

    /// <summary>What demo mode does, in the card body.</summary>
    /// <remarks>
    /// States the consequence rather than the mechanism. "Drops your system scopes and application roles"
    /// is accurate and means nothing to someone about to present to a customer.
    /// </remarks>
    public static readonly TextKey DemoDescription = new("team.simulation.card.demoDescription",
        "Hide your system-wide access and see this team as one of its members does. Your team access is unchanged.");

    public static readonly TextKey StartDemo = new("team.simulation.card.startDemo", "Start demo mode");

    public static readonly TextKey ViewAsUser = new("team.simulation.card.viewAsUser", "View as another user…");

    public static readonly TextKey ViewAsDescription = new("team.simulation.card.viewAsDescription",
        "See the application as a specific member, a role or an access level would see it.");

    /// <summary>Shown in place of the controls while something is already active.</summary>
    public static readonly TextKey ActiveNotice = new("team.simulation.card.activeNotice",
        "Your access is currently reduced.");

    public static readonly TextKey Stop = new("team.simulation.card.stop", "Return to my access");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
        [Title, Expand, Collapse, DemoMode, DemoDescription, StartDemo, ViewAsUser, ViewAsDescription, ActiveNotice, Stop];
}
