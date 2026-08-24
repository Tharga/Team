using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Scopes;

/// <summary>Localizable strings rendered by <c>ScopeView</c> — the interactive scope explorer.</summary>
/// <remarks>
/// The component is not yet fully migrated off literal text (Tharga/Team#204). This catalogue exists so
/// that strings added after the ratchet was set go through the text pipeline rather than adding to the
/// count — new work does not get to grow the backlog it is supposed to shrink.
/// </remarks>
public static class ScopeViewText
{
    /// <summary>Tooltip on the lock marking a grant-only scope.</summary>
    public static readonly TextKey GrantOnlyTooltip = new(
        "team.scopeView.grantOnlyTooltip",
        "Grant-only: no access level grants this scope, not even Owner or Administrator. It is held only through a role defined in code, and cannot be added to a custom role or a scope override here.");

    /// <summary>Legend line shown when at least one grant-only scope is registered.</summary>
    public static readonly TextKey GrantOnlyLegend = new(
        "team.scopeView.grantOnlyLegend",
        "A lock marks a grant-only scope: no access level grants it, so it stays greyed here unless a role you hold names it.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [GrantOnlyTooltip, GrantOnlyLegend];
}
