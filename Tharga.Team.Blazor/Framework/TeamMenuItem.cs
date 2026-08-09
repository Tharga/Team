using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// A host-supplied entry in the profile menu: an icon, a localizable label, and where it goes.
/// </summary>
/// <remarks>
/// <b>The label is a <see cref="TextKey"/>, not a string</b>, so an injected item localizes through the same
/// <see cref="IThargaTextProvider"/> the built-in entries use. A host already bridging that to its own content
/// system gets translated menu items with no extra work, and there is no second localization mechanism to keep
/// in step with the first.
/// </remarks>
/// <param name="Icon">Material icon name, e.g. <c>help</c>.</param>
/// <param name="Text">Label key and its English default.</param>
/// <param name="Href">Where the item navigates. Relative or absolute.</param>
/// <param name="RequiredScope">
/// Optional. When set, the item renders only for a caller holding this scope.
/// </param>
/// <param name="RequiredRole">
/// Optional. When set, the item renders only for a caller in this role. Combined with
/// <paramref name="RequiredScope"/> both must hold.
/// </param>
public sealed record TeamMenuItem(
    string Icon,
    TextKey Text,
    string Href,
    string RequiredScope = null,
    string RequiredRole = null);

/// <summary>
/// Whether a caller sees a given <see cref="TeamMenuItem"/>.
/// </summary>
/// <remarks>
/// <b>Rendering only — this is not an authorization decision.</b> Hiding a link the caller cannot use is a
/// courtesy; the page behind it still has to gate itself. Treating a hidden menu item as protection is exactly
/// the mistake the toolkit has already paid for once, so it is stated here rather than left to be assumed.
/// <para>
/// Scopes are read from the caller's team scope claims, so a scope-gated item follows the selected team the
/// same way the rest of the UI does.
/// </para>
/// </remarks>
public static class TeamMenuItemVisibility
{
    public static bool IsVisible(ClaimsPrincipal principal, TeamMenuItem item)
    {
        if (item == null) return false;
        if (item.RequiredScope == null && item.RequiredRole == null) return true;
        if (principal?.Identity is not { IsAuthenticated: true }) return false;

        if (item.RequiredScope != null && !HasScope(principal, item.RequiredScope)) return false;
        if (item.RequiredRole != null && !principal.IsInRole(item.RequiredRole)) return false;

        return true;
    }

    private static bool HasScope(ClaimsPrincipal principal, string scope)
        => principal.HasClaim(TeamClaimTypes.Scope, scope)
           || principal.HasClaim(TeamClaimTypes.SystemScope, scope);
}
