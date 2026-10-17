namespace Tharga.Team.Blazor.Features.User;

/// <summary>The tabs of <see cref="UsersView{TMember}"/>, for opening the view on one from a link.</summary>
public enum UsersViewTab
{
    Users,
    Teams,

    /// <summary>
    /// Users found in the directory only. Shown only to a caller with <c>users:manage</c> when a user
    /// directory is configured; asking for it otherwise opens <see cref="Users"/>.
    /// </summary>
    Directory
}

/// <summary>Maps <see cref="UsersViewTab"/> to the tab index, given whether the Directory tab is shown.</summary>
internal static class UsersViewTabs
{
    internal static int ToIndex(UsersViewTab tab, bool showDirectoryTab) => tab switch
    {
        UsersViewTab.Teams => 1,
        UsersViewTab.Directory when showDirectoryTab => 2,
        _ => 0
    };

    internal static UsersViewTab FromIndex(int index, bool showDirectoryTab) => index switch
    {
        1 => UsersViewTab.Teams,
        2 when showDirectoryTab => UsersViewTab.Directory,
        _ => UsersViewTab.Users
    };
}
