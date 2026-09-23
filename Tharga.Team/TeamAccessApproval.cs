namespace Tharga.Team;

/// <summary>
/// What approving an access request does to a team's consent.
/// </summary>
/// <remarks>
/// Pure, so the one part of approval that decides future access is testable on its own.
/// </remarks>
internal static class TeamAccessApproval
{
    /// <summary>
    /// When the granted consent ends, and the temporary consent to record — or nulls when the request asked for no end,
    /// which makes the consent standing.
    /// </summary>
    /// <remarks>
    /// <b>The previous consent is what the team returns to, so it must be the team's own standing consent.</b> Approving
    /// while an earlier temporary consent is still running keeps <i>that</i> consent's previous — otherwise the second
    /// window would record the first as the thing to return to, and the team would never get back to what it chose
    /// itself.
    /// </remarks>
    public static (DateTime? GrantedUntil, TemporaryConsent Temporary) Plan(ITeam team, TeamAccessRequest request, DateTime utcNow)
    {
        if (request.Duration == null) return (null, null);

        var grantedUntil = utcNow + request.Duration.Value;
        var running = team?.TemporaryConsent is { } current && utcNow < current.ExpiresAt ? current : null;

        if (running != null)
        {
            return (grantedUntil, new TemporaryConsent
            {
                ExpiresAt = grantedUntil,
                PreviousConsentedRoles = running.PreviousConsentedRoles,
                PreviousConsentAccessLevel = running.PreviousConsentAccessLevel
            });
        }

        var inForce = TeamConsent.Resolve(team, utcNow);

        return (grantedUntil, new TemporaryConsent
        {
            ExpiresAt = grantedUntil,
            PreviousConsentedRoles = inForce.HasConsent ? inForce.Roles : null,
            PreviousConsentAccessLevel = inForce.HasConsent ? inForce.AccessLevel : null
        });
    }
}
