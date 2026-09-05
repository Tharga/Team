namespace Tharga.Team;

/// <summary>
/// When an invitation expires. <b>The single copy of that rule.</b>
/// </summary>
/// <remarks>
/// Two places need the answer and they must not disagree: <c>TeamServiceBase.SetInvitationResponseAsync</c>
/// refuses an expired invitation, and <c>ITeamInvitationService.GetInvitationAsync</c> reports one as expired
/// so the screen can say so rather than showing a join button that throws.
/// <para>
/// Kept as one static because this codebase has already paid twice for the same rule living in two places —
/// most recently Tharga/Team#248, where a read gate recomputed what the claims builder had already decided
/// and the two drifted apart. A rule with two copies is a rule with a future defect.
/// </para>
/// </remarks>
public static class InvitationPolicy
{
    /// <summary>
    /// When <paramref name="invitation"/> stops being acceptable, or null if it never does.
    /// </summary>
    /// <remarks>
    /// The invitation's own <see cref="Invitation.ExpiresAt"/> wins when set — that is what an extension
    /// moves, and why extending does not have to reissue the code. Otherwise the configured
    /// <paramref name="lifetime"/> is measured from <see cref="Invitation.InviteTime"/>. No lifetime and no
    /// stored expiry means the invitation does not expire, which is the behaviour before any of this existed.
    /// </remarks>
    public static DateTime? ExpiresAt(Invitation invitation, TimeSpan? lifetime)
    {
        if (invitation == null) return null;
        if (invitation.ExpiresAt != null) return invitation.ExpiresAt;
        if (lifetime == null) return null;

        return invitation.InviteTime + lifetime.Value;
    }

    /// <summary>
    /// Whether <paramref name="invitation"/> has expired as at <paramref name="asAt"/>.
    /// </summary>
    /// <remarks>
    /// A null invitation is <b>not</b> expired. It means "nothing found", and the caller that looked it up
    /// answers that separately — reporting a missing invitation as an expired one would tell an unauthenticated
    /// visitor that a code they invented once existed.
    /// </remarks>
    public static bool HasExpired(Invitation invitation, TimeSpan? lifetime, DateTime asAt)
    {
        var expiresAt = ExpiresAt(invitation, lifetime);

        return expiresAt != null && expiresAt <= asAt;
    }
}
