namespace Tharga.Team;

/// <summary>
/// What happened to a received mail.
/// </summary>
public enum TeamMailChange
{
    /// <summary>The mail arrived, at a team or at the postmaster.</summary>
    Received,

    /// <summary>Somebody recorded what became of the mail.</summary>
    Handled,

    /// <summary>The postmaster routed the mail to a team.</summary>
    Rerouted,

    /// <summary>The postmaster put the mail away without routing it; it is still the postmaster's.</summary>
    Archived,

    /// <summary>The postmaster brought an archived mail back among the mail it holds.</summary>
    Restored
}
