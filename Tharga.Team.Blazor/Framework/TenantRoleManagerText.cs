namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>TenantRoleManager</c> — the editor for a team's own custom roles.</summary>
/// <remarks>
/// The three configuration notices used to wrap a setting name in <c>&lt;code&gt;</c>. A translatable sentence is
/// one string, and the house precedent is plain text with positional placeholders, so the setting names now sit
/// in the sentence unstyled. Wording is unchanged.
/// </remarks>
public static class TenantRoleManagerText
{
    /// <summary>Shown when the host registered no scopes, so there is nothing a role could grant.</summary>
    public static readonly TextKey NoScopesConfigured = new("team.tenantRoleManager.noScopesConfigured",
        "No scopes are configured. Register them via o.ConfigureScopes in Program.cs before defining custom roles.");

    /// <summary>Shown when dynamic tenant roles are switched off.</summary>
    public static readonly TextKey DynamicRolesDisabled = new("team.tenantRoleManager.dynamicRolesDisabled",
        "Dynamic tenant roles are not enabled. Set o.EnableDynamicRoles = true in AddThargaTeam.");

    /// <summary>Shown to a caller who cannot manage roles. <c>{0}</c> is the scope that does.</summary>
    public static readonly TextKey RequiresScope = new("team.tenantRoleManager.requiresScope",
        "Managing custom roles requires the {0} scope on this team.");

    public static readonly TextKey NoTeamSelected = new("team.tenantRoleManager.noTeamSelected", "No team selected.");

    public static readonly TextKey ColumnName = new("team.tenantRoleManager.columnName", "Name");
    public static readonly TextKey PlaceholderName = new("team.tenantRoleManager.placeholderName", "Role name");
    public static readonly TextKey ColumnDescription = new("team.tenantRoleManager.columnDescription", "Description");
    public static readonly TextKey PlaceholderDescription = new("team.tenantRoleManager.placeholderDescription", "Optional description");
    public static readonly TextKey ColumnScopes = new("team.tenantRoleManager.columnScopes", "Scopes");
    public static readonly TextKey PlaceholderScopes = new("team.tenantRoleManager.placeholderScopes", "Select scopes...");
    public static readonly TextKey DeleteRole = new("team.tenantRoleManager.deleteRole", "Delete role");
    public static readonly TextKey AddRole = new("team.tenantRoleManager.addRole", "Add role");
    public static readonly TextKey Save = new("team.tenantRoleManager.save", "Save");
    public static readonly TextKey Saved = new("team.tenantRoleManager.saved", "Custom roles saved.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        NoScopesConfigured, DynamicRolesDisabled, RequiresScope, NoTeamSelected, ColumnName, PlaceholderName,
        ColumnDescription, PlaceholderDescription, ColumnScopes, PlaceholderScopes, DeleteRole, AddRole, Save, Saved
    ];
}
