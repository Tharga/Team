namespace Tharga.Team;

/// <summary>
/// Why a received mail went where it did.
/// </summary>
public enum MailRouteReason
{
    /// <summary>Sent to a team's primary key.</summary>
    PrimaryKey,

    /// <summary>Sent to a team's secondary key, which is being retired; the team should tell the sender.</summary>
    RetiringKey,

    /// <summary>Sent to a key that has retired; kept by the postmaster.</summary>
    RetiredKey,

    /// <summary>Sent to an address that matches no key; kept by the postmaster.</summary>
    UnknownAddress,

    /// <summary>Kept by the postmaster first, then routed to a team by hand.</summary>
    Rerouted
}
