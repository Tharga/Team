namespace Tharga.Team;

/// <summary>
/// Which of a team's mail to list.
/// </summary>
public enum InboundMailFilter
{
    /// <summary>Mail nobody has handled yet.</summary>
    Unhandled,

    /// <summary>Mail somebody has handled.</summary>
    Handled,

    /// <summary>Every mail, handled or not.</summary>
    All
}
