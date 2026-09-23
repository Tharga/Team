using System.Security.Claims;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// Decides what an oversight caller (one holding <see cref="SystemTeamScopes.Read"/>) may see: whether
/// team listings widen beyond their own memberships, and how a team's consent level is presented.
/// </summary>
/// <remarks>
/// Pure and static so it is unit-testable — this project has no bUnit, so any decision left inside
/// razor markup is unreachable from tests. Mirrors <c>TeamActionGate</c> and
/// <c>CreateTeamActionResolver</c>.
/// </remarks>
internal static class TeamVisibility
{
    /// <summary>
    /// Whether the caller may enumerate every team. Keyed on the scope, never on a role name — role
    /// names are host-configurable, so hard-coding one would break for any host that renames them.
    /// </summary>
    public static bool CanSeeAllTeams(ClaimsPrincipal principal)
    {
        return TeamScopeGate.HasSystemScope(principal, SystemTeamScopes.Read);
    }

    /// <summary>
    /// The access level a team has actually consented to grant, or null when it has consented to no
    /// roles — visible, but no access — regardless of any level stored alongside.
    /// </summary>
    /// <param name="defaultAccessLevel">
    /// The host's configured <c>Consent.AccessLevel</c>, granted when the consent carries no level of its
    /// own. Passed in rather than assumed, because it is what <c>TeamMembershipClaimsBuilder</c> actually
    /// puts in the caller's claims — hard-coding Viewer here would misreport any host that changed it.
    /// </param>
    public static AccessLevel? Resolve(string[] consentedRoles, AccessLevel? consentAccessLevel, AccessLevel defaultAccessLevel)
    {
        if (consentedRoles is not { Length: > 0 }) return null;
        return consentAccessLevel ?? defaultAccessLevel;
    }

    /// <summary>
    /// The access level <paramref name="team"/> has consented to grant at <paramref name="utcNow"/>, through
    /// <see cref="TeamConsent.Resolve"/> — so a temporary consent reads as its previous consent once it has run out.
    /// </summary>
    public static AccessLevel? Resolve(ITeam team, AccessLevel defaultAccessLevel, DateTime utcNow)
    {
        var consent = TeamConsent.Resolve(team, utcNow);
        return Resolve(consent.Roles, consent.AccessLevel, defaultAccessLevel);
    }

    /// <summary>
    /// Label shown alongside the tint — colour alone is not an accessible encoding. Names the granted
    /// level rather than a coarse band, so "Viewer" and "User" are told apart.
    /// </summary>
    public static string Label(AccessLevel? consentAccessLevel) => consentAccessLevel switch
    {
        null => "No access",
        AccessLevel.Administrator => "Full access",
        var level => level.ToString()
    };

    /// <summary>
    /// Radzen <c>BadgeStyle</c> name for the tint. Returned as a string so this stays free of a
    /// component-library dependency and remains unit-testable. Theme-aware by construction — hard-coded
    /// colours would not survive dark theme.
    /// </summary>
    public static string BadgeStyle(AccessLevel? consentAccessLevel) => consentAccessLevel switch
    {
        null => "Danger",
        AccessLevel.Administrator => "Success",
        _ => "Warning"
    };
}
