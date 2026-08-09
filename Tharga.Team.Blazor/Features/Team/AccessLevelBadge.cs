using System.Security.Claims;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// The access-level badge shown beside the team selector: what the caller may actually do on the team
/// they have selected.
/// </summary>
/// <remarks>
/// Shown only to an oversight caller (one holding <see cref="SystemTeamScopes.Read"/>), who is the only
/// one whose access varies from team to team — an ordinary member holds the same level everywhere they
/// belong, so the badge would be noise. It reads the <see cref="TeamClaimTypes.AccessLevel"/> claim,
/// which <c>TeamMembershipClaimsBuilder</c> emits for the selected team from either the caller's
/// membership or the level that team consented to grant their role; an absent claim therefore means no
/// access at all, not a missing value.
/// Pure and static so it is unit-testable — this project has no bUnit, so any decision left inside razor
/// markup is unreachable from tests. Mirrors <see cref="TeamVisibility"/> and <c>TeamActionGate</c>.
/// </remarks>
internal static class AccessLevelBadge
{
    /// <summary>Whether to render the badge at all.</summary>
    public static bool ShouldShow(ClaimsPrincipal principal, bool hasSelectedTeam)
        => hasSelectedTeam && TeamVisibility.CanSeeAllTeams(principal);

    /// <summary>
    /// The caller's access level on the selected team, or null when the claim is absent — which reads
    /// as no access rather than as an unknown level.
    /// </summary>
    public static AccessLevel? Resolve(ClaimsPrincipal principal)
    {
        var value = principal?.FindFirst(TeamClaimTypes.AccessLevel)?.Value;
        return Enum.TryParse<AccessLevel>(value, out var level) ? level : null;
    }

    /// <summary>Badge text — the level name, or an explicit statement that there is no access.</summary>
    /// <remarks>
    /// Takes a resolved <see cref="TextSet"/> rather than awaiting a provider: this class is static and pure
    /// so it stays unit-testable, and the component has already resolved its strings in one pass. Passing
    /// <see cref="TextSet.Empty"/> yields the English defaults.
    /// </remarks>
    public static string Text(AccessLevel? accessLevel, TextSet text)
        => (text ?? TextSet.Empty)[AccessLevelText.For(accessLevel)];

    /// <summary>
    /// Radzen <c>BadgeStyle</c> name for the level. Returned as a string so this stays free of a
    /// component-library dependency and remains unit-testable. Theme-aware by construction — hard-coded
    /// colours would not survive dark theme.
    /// </summary>
    public static string Style(AccessLevel? accessLevel) => accessLevel switch
    {
        AccessLevel.Owner => "Success",
        AccessLevel.Administrator => "Info",
        AccessLevel.User => "Primary",
        AccessLevel.Viewer => "Light",
        AccessLevel.Custom => "Secondary",
        _ => "Danger"
    };
}
