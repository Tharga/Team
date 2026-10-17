namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>ApiKeyView</c>, and by <c>SystemApiKeyView</c> wherever the two say
/// the same thing.
/// </summary>
/// <remarks>
/// <b>Shared on purpose.</b> The two views are the same grid and create card over different key kinds, so a
/// column heading, a menu item or a confirmation is translated once and reads the same on both pages. What
/// only the system view says is in <see cref="SystemApiKeyViewText"/>.
/// </remarks>
public static class ApiKeyViewText
{
    /// <summary>Shown when no key management service is registered. <c>{0}</c> is the service interface to register.</summary>
    public static readonly TextKey NotConfigured = new("team.apiKeys.notConfigured", "API key management is not configured. Register {0} in Program.cs to enable this view.");

    public static readonly TextKey AccessDenied = new("team.apiKeys.accessDenied", "Access denied. You need the API key management permission to view this page.");

    public static readonly TextKey NoTeamSelected = new("team.apiKeys.noTeamSelected", "No team selected.");

    // Grid

    public static readonly TextKey Name = new("team.apiKeys.name", "Name");

    public static readonly TextKey Key = new("team.apiKeys.key", "Key");

    public static readonly TextKey AccessLevel = new("team.apiKeys.accessLevel", "Access Level");

    public static readonly TextKey Scopes = new("team.apiKeys.scopes", "Scopes");

    public static readonly TextKey LastUsed = new("team.apiKeys.lastUsed", "Last used");

    /// <summary>In the Last used column for a key that has never authenticated.</summary>
    public static readonly TextKey Never = new("team.apiKeys.never", "Never");

    public static readonly TextKey Tags = new("team.apiKeys.tags", "Tags");

    /// <summary>Tooltip on each row's menu button.</summary>
    public static readonly TextKey Actions = new("team.apiKeys.actions", "Actions");

    /// <summary>The checkbox on the create card, and the badge on a key that belongs to one person.</summary>
    public static readonly TextKey Personal = new("team.apiKeys.personal", "Personal");

    /// <summary>Tooltip on the Personal checkbox.</summary>
    public static readonly TextKey PersonalHelp = new("team.apiKeys.personalHelp", "Only you can see and manage this key.");

    /// <summary>Tooltip on the Personal badge in the list.</summary>
    public static readonly TextKey PersonalBadgeHelp = new("team.apiKeys.personalBadgeHelp", "A personal key: only its owner can see and manage it.");

    public static readonly TextKey Disabled = new("team.apiKeys.disabled", "Disabled");

    /// <summary>Tooltip on the Disabled badge. <c>{0}</c> is when.</summary>
    public static readonly TextKey DisabledAt = new("team.apiKeys.disabledAt", "Disabled {0}. This key is refused at authentication.");

    /// <summary>Tooltip on the Disabled badge. <c>{0}</c> is when, <c>{1}</c> who.</summary>
    public static readonly TextKey DisabledAtBy = new("team.apiKeys.disabledAtBy", "Disabled {0} by {1}. This key is refused at authentication.");

    /// <summary>Warning icon in the Last used column. <c>{0}</c> is the expiry date.</summary>
    public static readonly TextKey ExpiresSoon = new("team.apiKeys.expiresSoon", "Expires {0} — within 60 days");

    /// <summary>Tooltip on the Last used column. <c>{0}</c> created, <c>{1}</c> expires, <c>{2}</c> created by; one per line.</summary>
    public static readonly TextKey Dates = new("team.apiKeys.dates", "Created: {0}\nExpires: {1}\nCreated by: {2}");

    public static readonly TextKey NoExpiry = new("team.apiKeys.noExpiry", "No expiry");

    /// <summary>Who created a key that was created without a user, such as an auto-created key.</summary>
    public static readonly TextKey CreatedBySystem = new("team.apiKeys.createdBySystem", "System");

    /// <summary>Where a scope in the scope tooltip comes from: one of the key's own overrides.</summary>
    public static readonly TextKey ScopeFromOverride = new("team.apiKeys.scopeFromOverride", "override");

    /// <summary>Where a scope in the scope tooltip comes from: the key's access level.</summary>
    public static readonly TextKey ScopeFromAccessLevel = new("team.apiKeys.scopeFromAccessLevel", "access level");

    /// <summary>Where a scope in the scope tooltip comes from: one of the key's roles.</summary>
    public static readonly TextKey ScopeFromRole = new("team.apiKeys.scopeFromRole", "role");

    // Create card

    public static readonly TextKey KeyNamePlaceholder = new("team.apiKeys.keyNamePlaceholder", "Key name");

    public static readonly TextKey Roles = new("team.apiKeys.roles", "Roles");

    public static readonly TextKey ExpiryDate = new("team.apiKeys.expiryDate", "Expiry Date");

    public static readonly TextKey Create = new("team.apiKeys.create", "Create");

    public static readonly TextKey Created = new("team.apiKeys.created", "API key created");

    public static readonly TextKey CreateFailed = new("team.apiKeys.createFailed", "Failed to create key");

    /// <summary>Title of the dialog revealing a new or regenerated key. <c>{0}</c> is the key's name.</summary>
    public static readonly TextKey RevealTitle = new("team.apiKeys.revealTitle", "API key — {0}");

    // Row menu

    public static readonly TextKey Copy = new("team.apiKeys.copy", "Copy");

    public static readonly TextKey ShowValue = new("team.apiKeys.showValue", "Show value");

    public static readonly TextKey HideValue = new("team.apiKeys.hideValue", "Hide value");

    public static readonly TextKey AuditLog = new("team.apiKeys.auditLog", "Audit log");

    public static readonly TextKey EditRolesAndScopes = new("team.apiKeys.editRolesAndScopes", "Edit roles & scopes");

    public static readonly TextKey Lock = new("team.apiKeys.lock", "Lock");

    public static readonly TextKey Enable = new("team.apiKeys.enable", "Enable");

    public static readonly TextKey Disable = new("team.apiKeys.disable", "Disable");

    public static readonly TextKey Refresh = new("team.apiKeys.refresh", "Refresh");

    public static readonly TextKey Delete = new("team.apiKeys.delete", "Delete");

    // Row actions

    /// <summary><c>{0}</c> is the key's name.</summary>
    public static readonly TextKey Copied = new("team.apiKeys.copied", "The {0} key has been copied to the clipboard.");

    /// <summary><c>{0}</c> is the key's name.</summary>
    public static readonly TextKey ConfirmRegenerate = new("team.apiKeys.confirmRegenerate", "The {0} key will be regenerated.");

    public static readonly TextKey ConfirmRegenerateTitle = new("team.apiKeys.confirmRegenerateTitle", "Confirm regenerate API key");

    /// <summary><c>{0}</c> is the key's name.</summary>
    public static readonly TextKey ConfirmLock = new("team.apiKeys.confirmLock", "The {0} key will be locked. After that, it cannot be retrieved.");

    public static readonly TextKey ConfirmLockTitle = new("team.apiKeys.confirmLockTitle", "Confirm lock API key");

    /// <summary><c>{0}</c> is the key's name.</summary>
    public static readonly TextKey ConfirmDisable = new("team.apiKeys.confirmDisable", "Disable API key '{0}'? It stops working immediately, and keeps its name, scopes, roles and history so it can be enabled again.");

    public static readonly TextKey ConfirmDisableTitle = new("team.apiKeys.confirmDisableTitle", "Confirm disable");

    /// <summary><c>{0}</c> is the key's name.</summary>
    public static readonly TextKey ConfirmDelete = new("team.apiKeys.confirmDelete", "Delete API key '{0}'? This cannot be undone.");

    public static readonly TextKey ConfirmDeleteTitle = new("team.apiKeys.confirmDeleteTitle", "Confirm delete");

    public static readonly TextKey Deleted = new("team.apiKeys.deleted", "API key deleted");

    public static readonly TextKey DeleteFailed = new("team.apiKeys.deleteFailed", "Failed to delete key");

    /// <summary>Title of the key's audit log dialog. <c>{0}</c> is the key's name.</summary>
    public static readonly TextKey AuditLogTitle = new("team.apiKeys.auditLogTitle", "Audit log — {0}");

    // Edit dialog

    /// <summary><c>{0}</c> is the key's name.</summary>
    public static readonly TextKey EditTitle = new("team.apiKeys.editTitle", "Edit — {0}");

    public static readonly TextKey ScopeOverrides = new("team.apiKeys.scopeOverrides", "Scope Overrides");

    public static readonly TextKey Save = new("team.apiKeys.save", "Save");

    public static readonly TextKey Cancel = new("team.apiKeys.cancel", "Cancel");

    public static readonly TextKey Updated = new("team.apiKeys.updated", "API key updated");

    public static readonly TextKey UpdateFailed = new("team.apiKeys.updateFailed", "Failed to update key");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        NotConfigured, AccessDenied, NoTeamSelected,
        Name, Key, AccessLevel, Scopes, LastUsed, Never, Tags, Actions, Personal, PersonalHelp, PersonalBadgeHelp,
        Disabled, DisabledAt, DisabledAtBy, ExpiresSoon, Dates, NoExpiry, CreatedBySystem,
        ScopeFromOverride, ScopeFromAccessLevel, ScopeFromRole,
        KeyNamePlaceholder, Roles, ExpiryDate, Create, Created, CreateFailed, RevealTitle,
        Copy, ShowValue, HideValue, AuditLog, EditRolesAndScopes, Lock, Enable, Disable, Refresh, Delete,
        Copied, ConfirmRegenerate, ConfirmRegenerateTitle, ConfirmLock, ConfirmLockTitle, ConfirmDisable, ConfirmDisableTitle,
        ConfirmDelete, ConfirmDeleteTitle, Deleted, DeleteFailed, AuditLogTitle,
        EditTitle, ScopeOverrides, Save, Cancel, Updated, UpdateFailed
    ];
}
