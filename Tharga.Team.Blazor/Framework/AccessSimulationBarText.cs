namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>AccessSimulationBar</c> — the banner shown while a simulation is
/// active, and the entry point offered when none is.
/// </summary>
/// <remarks>
/// Separate from <see cref="AccessSimulationCardText"/> for the reason stated there: the bar warns, the
/// card offers, and sharing keys would force one phrasing to serve both.
/// <para>
/// <b><see cref="ViewingAs"/> is one sentence with a placeholder, not two fragments around a name.</b>
/// A translator has to be able to move the target within the sentence — Swedish and English do not agree on
/// where it goes — and a "viewing as" prefix plus a "your access is reduced" suffix hard-codes English word
/// order into every translation. The same reasoning is why <see cref="TargetRole"/> and
/// <see cref="TargetAccessLevel"/> are whole phrases rather than a shared "the" and "role".
/// </para>
/// <para>
/// A translation that omits <c>{0}</c> renders the sentence without naming the target rather than failing —
/// the banner is the way out of a reduced session, so it has to render whatever it was given.
/// </para>
/// </remarks>
public static class AccessSimulationBarText
{
    /// <summary>The banner sentence. <c>{0}</c> is what is being simulated.</summary>
    public static readonly TextKey ViewingAs = new("team.simulation.bar.viewingAs",
        "Viewing as {0} — your own access is reduced.");

    /// <summary>The way out. Named to match <see cref="AccessSimulationCardText.Stop"/>, which does the same thing.</summary>
    public static readonly TextKey Stop = new("team.simulation.bar.stop", "Return to my access");

    /// <summary>The entry-point button, when nothing is being simulated.</summary>
    public static readonly TextKey ViewAs = new("team.simulation.bar.viewAs", "View as…");

    /// <summary>A simulated tenant role, named in the banner sentence. <c>{0}</c> is the role name.</summary>
    public static readonly TextKey TargetRole = new("team.simulation.bar.targetRole", "the {0} role");

    /// <summary>A simulated access level, named in the banner sentence. <c>{0}</c> is the level.</summary>
    public static readonly TextKey TargetAccessLevel = new("team.simulation.bar.targetAccessLevel", "access level {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [ViewingAs, Stop, ViewAs, TargetRole, TargetAccessLevel];
}
