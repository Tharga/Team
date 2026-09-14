namespace Tharga.Team;

/// <summary>An access request together with the team it is for.</summary>
/// <param name="TeamKey">The team.</param>
/// <param name="TeamName">The team's name, for display.</param>
/// <param name="Request">The request.</param>
public sealed record TeamAccessRequestItem(string TeamKey, string TeamName, TeamAccessRequest Request);

/// <summary>
/// Requesting access to teams, and seeing requests across teams — <b>the interface a component, controller or MCP
/// provider should inject</b> for them. Filtered rather than scope-gated.
/// </summary>
/// <remarks>
/// Separate from <see cref="ITeamManagementService"/>, which is gated and wholly team-bound, for two reasons. Requesting
/// is authorized by holding a consent role rather than a scope — the requester holds nothing on the team yet — and the
/// two cross-team reads name no team, so they cannot be gated by the team a call names. Each read recomputes what the
/// caller may see per team instead: their own requests, or requests on teams where their membership grants
/// <c>team:manage</c>.
/// <para>
/// Approving and denying are on <see cref="ITeamManagementService"/>, gated on <c>team:manage</c>.
/// </para>
/// </remarks>
public interface ITeamAccessRequestService
{
    /// <summary>
    /// Asks for access to a team the caller is not a member of. Requires one of the configured consent roles. Replaces the
    /// caller's pending request on that team, if any.
    /// </summary>
    Task<TeamAccessRequest> RequestTeamAccessAsync(string teamKey, AccessLevel accessLevel, TimeSpan? duration, string message);

    /// <summary>Withdraws the caller's own pending request.</summary>
    Task CancelTeamAccessRequestAsync(string teamKey, string requestId);

    /// <summary>
    /// The caller's own requests across the teams they can see, newest first.
    /// </summary>
    /// <remarks>
    /// Read from every team when the caller holds <c>teams:read</c> — which a requester needs anyway, to find a team they
    /// are not in — and from no team otherwise. Only requests the caller made are returned.
    /// </remarks>
    Task<IReadOnlyList<TeamAccessRequestItem>> GetMyAccessRequestsAsync();

    /// <summary>
    /// Pending requests the caller can decide: on teams where their membership grants <c>team:manage</c>, excluding
    /// their own. Newest first.
    /// </summary>
    Task<IReadOnlyList<TeamAccessRequestItem>> GetAccessRequestsAwaitingMeAsync();
}
