namespace Tharga.Team;

/// <summary>
/// What a support person says about being there to answer, which members are shown through <see cref="ISupportPresence"/>.
/// </summary>
/// <remarks>
/// Their choice, adjusted by what they do: <see cref="Online"/> counts as away once they have done nothing in support
/// for a while, and counts again as soon as they act. Kept in-process, as typing is, so nothing about it is stored.
/// </remarks>
public enum SupportStaffStatus
{
    /// <summary>There to answer, and counted as online while active in support.</summary>
    Online,

    /// <summary>Not answering now. Members are told support will reply later.</summary>
    Away,

    /// <summary>
    /// Answering as usual without being counted: with only Hidden people on the desk, members are told nothing about
    /// support, neither online nor that it will reply later.
    /// </summary>
    Hidden
}
