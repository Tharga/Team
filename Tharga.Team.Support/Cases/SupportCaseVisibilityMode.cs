namespace Tharga.Team.Support.Cases;

/// <summary>
/// Whether a team's support readers reach every case in the team, or only those their authors share.
/// </summary>
public enum SupportCaseVisibilityMode
{
    /// <summary>Every case is readable by the team's <c>support:read</c> holders. The behaviour before per-case visibility existed.</summary>
    TeamWide,

    /// <summary>A case starts private; its author may share it with the team while it is open.</summary>
    PerCase
}
