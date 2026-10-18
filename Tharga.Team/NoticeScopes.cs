namespace Tharga.Team;

/// <summary>Team scope constants for automatic notices.</summary>
/// <remarks>
/// <b>One scope, for a team's defaults.</b> A user's own choices are authorized by being that user, and raising is
/// server-side, so the only team decision is what members receive until they choose. It is granted to administrators
/// by default, which the host can change or make grant-only.
/// </remarks>
public static class NoticeScopes
{
    /// <summary>Set the team's defaults for automatic notices.</summary>
    public const string Manage = "notices:manage";
}
