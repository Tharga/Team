namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>AccessSimulationDialog</c> — the screen that composes the access to view as.
/// </summary>
/// <remarks>
/// Separate from <see cref="AccessSimulationCardText"/> and <see cref="AccessSimulationBarText"/> for the reason
/// stated on the card: each says different things to a different moment. The dialog's title stays
/// <see cref="AccessSimulationCardText.ViewAsUser"/>, which both entry points already open it under.
/// <para>
/// The composed <c>AccessSimulation.Label</c> is <b>not</b> built from these. It is written to audit metadata, where
/// a value that varies by the operator's language cannot be searched or compared.
/// </para>
/// </remarks>
public static class AccessSimulationDialogText
{
    public static readonly TextKey Intro = new("team.simulation.dialog.intro",
        "See the application with less access. Anything you do is still recorded as you.");

    /// <summary>The limitation, stated where the choice is made.</summary>
    public static readonly TextKey Limitation = new("team.simulation.dialog.limitation",
        "This can only reduce your access, never add to it. Scopes you do not hold are listed but cannot be kept.");

    public static readonly TextKey Member = new("team.simulation.dialog.member", "Start from a member");

    public static readonly TextKey MemberPlaceholder = new("team.simulation.dialog.memberPlaceholder", "Choose a member…");

    public static readonly TextKey AccessLevel = new("team.simulation.dialog.accessLevel", "Access level");

    public static readonly TextKey AccessLevelPlaceholder = new("team.simulation.dialog.accessLevelPlaceholder", "No access level");

    public static readonly TextKey Roles = new("team.simulation.dialog.roles", "Roles");

    public static readonly TextKey RolesPlaceholder = new("team.simulation.dialog.rolesPlaceholder", "Add roles…");

    public static readonly TextKey Scopes = new("team.simulation.dialog.scopes", "Scopes");

    public static readonly TextKey ScopeSearch = new("team.simulation.dialog.scopeSearch", "Search scopes…");

    /// <summary>Marks a scope the chosen access level or roles already grant.</summary>
    public static readonly TextKey Inherited = new("team.simulation.dialog.inherited", "from level or role");

    /// <summary>Marks a scope the caller does not hold.</summary>
    public static readonly TextKey NotHeld = new("team.simulation.dialog.notHeld", "you do not hold this");

    /// <summary>Summary under the list. {0} = kept, {1} = selected.</summary>
    public static readonly TextKey Summary = new("team.simulation.dialog.summary", "{0} of {1} selected scopes will be kept.");

    public static readonly TextKey GapTitle = new("team.simulation.dialog.gapTitle", "This will not be an exact view");

    /// <summary>{0} = the scopes the caller does not hold.</summary>
    public static readonly TextKey GapUnreachable = new("team.simulation.dialog.gapUnreachable",
        "You do not hold {0} yourself, so the simulation cannot show it. The real access may allow more than you are about to see.");

    public static readonly TextKey GapSystem = new("team.simulation.dialog.gapSystem",
        "System-wide access comes from application roles, which are not stored here, so it is dropped rather than reproduced. This shows access within this team.");

    public static readonly TextKey Faithful = new("team.simulation.dialog.faithful", "You hold everything selected, so this will be an exact view.");

    public static readonly TextKey Start = new("team.simulation.dialog.start", "Start");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Intro, Limitation, Member, MemberPlaceholder, AccessLevel, AccessLevelPlaceholder, Roles, RolesPlaceholder,
        Scopes, ScopeSearch, Inherited, NotHeld, Summary, GapTitle, GapUnreachable, GapSystem, Faithful, Start
    ];
}
