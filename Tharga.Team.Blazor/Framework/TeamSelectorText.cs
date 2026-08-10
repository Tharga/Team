using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>TeamSelector</c>, beyond the shared menu strings.</summary>
public static class TeamSelectorText
{
    public static readonly TextKey Suspended = new("team.selector.suspended", "Suspended");
    public static readonly TextKey AccessLevelTooltip = new("team.selector.accessLevelTooltip", "Your access level on the selected team");
    public static readonly TextKey SuspendedTitle = new("team.selector.suspendedTitle", "Your access to this team is suspended. Select it to see what that means.");

    /// <summary>
    /// The picker's placeholder while no team is selected — the prompt an oversight caller who belongs to
    /// no team is shown, where the top bar used to be empty.
    /// </summary>
    public static readonly TextKey SelectTeam = new("team.selector.selectTeam", "Select a team");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Suspended, AccessLevelTooltip, SuspendedTitle, SelectTeam];
}

/// <summary>
/// Localizable names for <see cref="AccessLevel"/>, and for having none.
/// </summary>
/// <remarks>
/// <b>Separate from any one component, because more than one renders them.</b> A level named differently in
/// the selector than in the member grid would read as two different things.
/// <para>
/// Keyed per level rather than derived from <c>Enum.ToString()</c>: a translation cannot be reached from an
/// enum member name, and a consumer renaming the tenant concept usually renames these too — "Owner" of an
/// Organisation may not be the word they want.
/// </para>
/// </remarks>
public static class AccessLevelText
{
    public static readonly TextKey Owner = new("team.accesslevel.owner", "Owner");
    public static readonly TextKey Administrator = new("team.accesslevel.administrator", "Administrator");
    public static readonly TextKey User = new("team.accesslevel.user", "User");
    public static readonly TextKey Viewer = new("team.accesslevel.viewer", "Viewer");
    public static readonly TextKey Custom = new("team.accesslevel.custom", "Custom");
    public static readonly TextKey None = new("team.accesslevel.none", "No access");

    /// <summary>The key naming <paramref name="accessLevel"/>, or <see cref="None"/> when there is no level.</summary>
    /// <remarks>
    /// An unrecognised level also maps to <see cref="None"/>, matching the existing badge behaviour: an
    /// absent or unparseable access-level claim means no access, not an unknown one.
    /// </remarks>
    public static TextKey For(AccessLevel? accessLevel) => accessLevel switch
    {
        AccessLevel.Owner => Owner,
        AccessLevel.Administrator => Administrator,
        AccessLevel.User => User,
        AccessLevel.Viewer => Viewer,
        AccessLevel.Custom => Custom,
        _ => None
    };

    /// <summary>Every key here, for a component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Owner, Administrator, User, Viewer, Custom, None];
}
