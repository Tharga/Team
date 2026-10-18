namespace Tharga.Team;

/// <summary>
/// Which of the postmaster's mail to list.
/// </summary>
public enum PostmasterMailState
{
    /// <summary>Mail waiting for someone to route or archive it — what the postmaster counts.</summary>
    Held,

    /// <summary>Mail somebody put away without routing it.</summary>
    Archived
}
