namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings for the team profile menu (<c>LoginDisplay</c>) and <c>TeamSelector</c>.
/// Each entry carries its stable key and English default; pass them to <see cref="IThargaTextProvider.GetAsync"/>.
/// </summary>
public static class TeamMenuText
{
    /// <summary>The menu entry for the signed-in user's profile.</summary>
    public static readonly TextKey User = new("team.menu.user", "User");

    /// <summary>The menu entry for the selected team.</summary>
    public static readonly TextKey Team = new("team.menu.team", "Team");

    /// <summary>The menu entry that signs out.</summary>
    public static readonly TextKey Logout = new("team.menu.logout", "Logout");

    /// <summary>The link that signs in.</summary>
    public static readonly TextKey Login = new("team.menu.login", "Login");

    /// <summary>The menu entry that creates a team.</summary>
    public static readonly TextKey CreateTeam = new("team.menu.createTeam", "Create Team");

    /// <summary>Shown while the menu loads.</summary>
    public static readonly TextKey Loading = new("team.menu.loading", "Loading...");

    /// <summary>Every key here.</summary>
    public static readonly TextKey[] All = [User, Team, Logout, Login, CreateTeam, Loading];
}
