namespace Tharga.Team;

/// <summary>
/// Who in a case's team, besides its author, may read it — when the host lets each case decide.
/// </summary>
/// <remarks>
/// Only consulted when the host enables per-case visibility. The author and the product's own support
/// staff reach a case whatever it says; this governs the team's support readers alone.
/// </remarks>
public enum SupportCaseVisibility
{
    /// <summary>The team's support readers may read and answer it.</summary>
    Team,

    /// <summary>Between the author and the product's support staff.</summary>
    Private
}
