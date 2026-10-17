namespace Tharga.Team;

/// <summary>
/// Why a support case that arrived by mail suggests teams instead of having opened in one.
/// </summary>
public enum SupportTeamSuggestionReason
{
    /// <summary>The sender is verified and an active member of more than one team.</summary>
    SeveralTeams,

    /// <summary>The sender's address belongs to members of these teams, but the mail was not verified as coming from it.</summary>
    SenderNotVerified,

    /// <summary>More than one user account holds the sender's address, so the mail cannot be put to one of them.</summary>
    SharedAddress
}
