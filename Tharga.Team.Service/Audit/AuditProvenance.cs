using System.Security.Claims;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// How a caller came to be in the team an audit entry is about, for a host that writes its own entries.
/// </summary>
/// <remarks>
/// The enforcement proxies stamp <see cref="AuditMetadataKeys.AuthorizedVia"/> on what they write. An entry a host builds
/// through <see cref="IAuditEntryFactory"/> carries only the metadata the host gives it, so a host recording a read of its
/// own asks here and stamps <see cref="AuditMetadataKeys.AuthorizedViaConsent"/> itself — otherwise
/// <see cref="AuditOptions.RecordConsentedAccess"/> cannot tell that entry from a member's.
/// </remarks>
public static class AuditProvenance
{
    /// <summary>
    /// Whether <paramref name="principal"/> reached <paramref name="teamKey"/> through the team's consent rather than a
    /// membership or a key issued for that team.
    /// </summary>
    public static bool IsConsented(ClaimsPrincipal principal, string teamKey)
        => TeamScopePolicy.HasConsentedAccess(principal, teamKey);
}
