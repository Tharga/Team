using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Scopes;

/// <summary>Localizable strings rendered by <c>ScopeView</c> — the interactive scope explorer.</summary>
/// <remarks>
/// Every string the component renders is here, so a host can translate or reword the page (Tharga/Team#204,
/// #401). Inline emphasis and code styling are not carried in the values: a translatable sentence is one string,
/// with positional placeholders where the component fills something in.
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

    /// <summary>Tooltip on the marker of a scope the access level grants to members only (Tharga/Team#412).</summary>
    public static readonly TextKey MembersOnlyTooltip = new(
        "team.scopeView.membersOnlyTooltip",
        "Members only: the access level grants this scope to members, not to API keys. A key holds it only when a role or an override on the key names it.");

    /// <summary>Legend line shown when at least one members-only scope is registered.</summary>
    public static readonly TextKey MembersOnlyLegend = new(
        "team.scopeView.membersOnlyLegend",
        "A person marks a members-only scope: members at the level hold it, API keys at the same level do not.");

    /// <summary>Heading over the scopes that belong to no group, shown only when the host defined groups.</summary>
    public static readonly TextKey Ungrouped = new("team.scopeView.ungrouped", "Other");

    /// <summary>Shown when the host registered no scopes at all.</summary>
    public static readonly TextKey NoScopesConfigured = new(
        "team.scopeView.noScopesConfigured",
        "No scopes are configured. Register them via o.ConfigureScopes in Program.cs.");

    public static readonly TextKey TeamScopesHeading = new("team.scopeView.teamScopesHeading", "Team scopes");
    public static readonly TextKey AccessLevel = new("team.scopeView.accessLevel", "Access level");
    public static readonly TextKey Roles = new("team.scopeView.roles", "Roles");

    /// <summary>Opening line of the explanation under the selectors.</summary>
    public static readonly TextKey Explanation = new(
        "team.scopeView.explanation",
        "Scopes granted by the selected access level and roles are shown in full; greyed rows are not granted.");

    /// <summary>Added when the selectors were pre-set from the caller's membership. <c>{0}</c> is the text of the Override badge.</summary>
    public static readonly TextKey LoadedFromMember = new(
        "team.scopeView.loadedFromMember",
        "Pre-selected to your current membership — {0} marks your personal scope overrides.");

    /// <summary>Tooltip on the star marking a scope the caller holds through a personal override.</summary>
    public static readonly TextKey PersonalOverrideTooltip = new(
        "team.scopeView.personalOverrideTooltip",
        "You have a personal scope override for this scope.");

    public static readonly TextKey ColumnScope = new("team.scopeView.columnScope", "Scope");
    public static readonly TextKey ColumnDescription = new("team.scopeView.columnDescription", "Description");
    public static readonly TextKey ColumnGrantedBy = new("team.scopeView.columnGrantedBy", "Granted by");

    /// <summary>Badge on a scope the caller holds through a personal override.</summary>
    public static readonly TextKey Override = new("team.scopeView.override", "Override");

    public static readonly TextKey SystemScopesHeading = new("team.scopeView.systemScopesHeading", "System scopes");

    public static readonly TextKey SystemScopesExplanation = new(
        "team.scopeView.systemScopesExplanation",
        "Global capabilities you currently hold — granted to system API keys and via privileged roles.");

    /// <summary>The selector label for the Administrator level, which resolves identically to Owner.</summary>
    public static readonly TextKey LevelOwnerAdministrator = new("team.scopeView.levelOwnerAdministrator", "Owner / Administrator");

    /// <summary>The same level, as a short badge in the "Granted by" column.</summary>
    public static readonly TextKey LevelOwnerAdmin = new("team.scopeView.levelOwnerAdmin", "Owner / Admin");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        GrantOnlyTooltip, GrantOnlyLegend, MembersOnlyTooltip, MembersOnlyLegend, Ungrouped,
        NoScopesConfigured, TeamScopesHeading, AccessLevel, Roles, Explanation, LoadedFromMember, PersonalOverrideTooltip,
        ColumnScope, ColumnDescription, ColumnGrantedBy, Override, SystemScopesHeading, SystemScopesExplanation,
        LevelOwnerAdministrator, LevelOwnerAdmin
    ];
}
