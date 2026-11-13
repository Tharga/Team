namespace Tharga.Team;

/// <summary>
/// What a support person says about being there to answer, which members are shown through <see cref="ISupportPresence"/>.
/// </summary>
/// <remarks>
/// Their choice, adjusted by what they do: <see cref="Automatic"/> counts as away once they have done nothing in support
/// for a while, and counts again as soon as they act. Kept in-process, as typing is, so nothing about it is stored.
/// <para>
/// The numbers are fixed, because a choice is remembered in the browser as a number: what used to be Online (0) is now
/// <see cref="Automatic"/>, so nobody's behaviour changes on upgrade.
/// </para>
/// </remarks>
public enum SupportStaffStatus
{
    /// <summary>
    /// Online while active in support, and away after <c>SupportCaseOptions.StaffIdleAfter</c> without activity. What
    /// <b>Online</b> meant before <see cref="Online"/> was added, and the status a new person starts on.
    /// </summary>
    Automatic = 0,

    /// <summary>Not answering now. Members are told support will reply later.</summary>
    Away = 1,

    /// <summary>
    /// Answering as usual without being counted: with only Hidden people on the desk, members are told nothing about
    /// support, neither online nor that it will reply later.
    /// </summary>
    Hidden = 2,

    /// <summary>
    /// Online for as long as the choice stands, active or not, until <c>SupportCaseOptions.StaffOnlineLapseAfter</c> passes
    /// without them doing anything.
    /// </summary>
    Online = 3
}
