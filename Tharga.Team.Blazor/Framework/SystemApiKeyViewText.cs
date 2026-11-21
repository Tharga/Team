namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings only <c>SystemApiKeyView</c> renders. Everything it shares with the team view —
/// columns, menu items, most confirmations — is in <see cref="ApiKeyViewText"/>.
/// </summary>
public static class SystemApiKeyViewText
{
    public static readonly TextKey AccessDenied = new("team.systemApiKeys.accessDenied", "Access denied. The Developer role is required to manage system API keys.");

    /// <summary>Placeholder of the scope picker on the create card.</summary>
    public static readonly TextKey SelectScopes = new("team.systemApiKeys.selectScopes", "Select scopes...");

    public static readonly TextKey Created = new("team.systemApiKeys.created", "System API key created");

    /// <summary>The row-menu item that changes a system key's scopes without changing its value.</summary>
    public static readonly TextKey EditScopes = new("team.systemApiKeys.editScopes", "Edit scopes");

    /// <summary>Title of the dialog revealing a new or regenerated key. <c>{0}</c> is the key's name.</summary>
    public static readonly TextKey RevealTitle = new("team.systemApiKeys.revealTitle", "System API key — {0}");

    /// <summary>Unlike the team key's, says nothing of roles: a system key has none. <c>{0}</c> is the key's name.</summary>
    public static readonly TextKey ConfirmDisable = new("team.systemApiKeys.confirmDisable", "Disable API key '{0}'? It stops working immediately, and keeps its name, scopes and history so it can be enabled again.");

    /// <summary><c>{0}</c> is the key's name.</summary>
    public static readonly TextKey ConfirmDelete = new("team.systemApiKeys.confirmDelete", "Delete system API key '{0}'? This cannot be undone.");

    public static readonly TextKey Deleted = new("team.systemApiKeys.deleted", "System API key deleted");

    /// <summary>Why a key's row offers no change: it holds scopes the caller does not. Shown as the tooltip of the row's actions button. <c>{0}</c> lists those scopes.</summary>
    public static readonly TextKey AboveYou = new("team.systemApiKeys.aboveYou", "This key holds scopes you do not hold yourself ({0}), so it is not yours to change.");

    /// <summary>The tooltip of a scope in the picker that the caller cannot grant, because they do not hold it themselves.</summary>
    public static readonly TextKey NotHeld = new("team.systemApiKeys.notHeld", "You do not hold this scope yourself, so you cannot grant it.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/> together with <see cref="ApiKeyViewText.All"/>.</summary>
    public static readonly TextKey[] All =
    [
        AccessDenied, SelectScopes, Created, EditScopes, RevealTitle, ConfirmDisable, ConfirmDelete, Deleted, AboveYou, NotHeld
    ];
}
