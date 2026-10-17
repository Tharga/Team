namespace Tharga.Team.Blazor.Features.User;

/// <summary>
/// Membership info for a user across teams, surfaced in the user drill-down panel.
/// </summary>
public record UserTeamInfo
{
    public string TeamKey { get; init; }
    public string TeamName { get; init; }
    /// <summary>The member's level, or null when none was ever set (grants nothing).</summary>
    public AccessLevel? AccessLevel { get; init; }
    public MembershipState? State { get; init; }
}
