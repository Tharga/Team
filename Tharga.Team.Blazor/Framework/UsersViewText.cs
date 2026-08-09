namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>UsersView</c> — the administration tab strip.</summary>
/// <remarks>
/// <b><see cref="TeamsTab"/> is the tenant noun in plural</b>, and is the reason a consumer who calls the
/// tenant something else — Organisation, Space — needs this at all. It is keyed whole rather than composed
/// from a shared "team" token: see <see cref="ThargaTextKeys"/> for why a substitutable noun does not survive
/// translation.
/// </remarks>
public static class UsersViewText
{
    public static readonly TextKey UsersTab = new("team.usersView.usersTab", "Users");
    public static readonly TextKey TeamsTab = new("team.usersView.teamsTab", "Teams");
    public static readonly TextKey DirectoryOnlyTab = new("team.usersView.directoryOnlyTab", "Users in directory only");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [UsersTab, TeamsTab, DirectoryOnlyTab];
}
