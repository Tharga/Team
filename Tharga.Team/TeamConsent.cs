namespace Tharga.Team;

/// <summary>The consent a team has in force at a moment.</summary>
/// <param name="Roles">The roles consented to. Never null; empty means no consent.</param>
/// <param name="AccessLevel">The consented level, or null to fall back to the configured default.</param>
/// <param name="ExpiresAt">When this consent runs out, or null when it is standing.</param>
public sealed record ConsentInForce(string[] Roles, AccessLevel? AccessLevel, DateTime? ExpiresAt)
{
    /// <summary>Whether any role is consented to.</summary>
    public bool HasConsent => Roles.Length > 0;

    /// <summary>Whether a caller holding <paramref name="roles"/> is covered by this consent.</summary>
    public bool Covers(IEnumerable<string> roles) => HasConsent && (roles ?? []).Any(r => Roles.Contains(r, StringComparer.Ordinal));
}

/// <summary>
/// <b>The one rule for which consent a team has in force.</b> Every consent read goes through it.
/// </summary>
/// <remarks>
/// A team's stored consent can be temporary — set by an approved access request and due to return to what it was.
/// Reading <see cref="ITeam.ConsentedRoles"/> or <see cref="ITeam.ConsentAccessLevel"/> directly would keep honouring
/// it after it ran out. Resolving here instead is what makes expiry take effect without a background job: the claims
/// builder, the API-key team context and every badge ask this, so the next read after expiry sees the previous
/// consent.
/// </remarks>
public static class TeamConsent
{
    /// <summary>The consent <paramref name="team"/> has in force at <paramref name="utcNow"/>.</summary>
    public static ConsentInForce Resolve(ITeam team, DateTime utcNow)
    {
        if (team == null) return new ConsentInForce([], null, null);

        var temporary = team.TemporaryConsent;

        if (temporary != null && utcNow >= temporary.ExpiresAt)
            return new ConsentInForce(temporary.PreviousConsentedRoles ?? [], temporary.PreviousConsentAccessLevel, null);

        return new ConsentInForce(team.ConsentedRoles ?? [], team.ConsentAccessLevel, temporary?.ExpiresAt);
    }
}
