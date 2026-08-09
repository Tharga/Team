namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>RoleEditor</c> — the tenant-role picker.</summary>
public static class RoleEditorText
{
    public static readonly TextKey Placeholder = new("team.roleEditor.placeholder", "Select roles...");

    /// <summary>Tooltip prefix. Placeholder: the comma-separated scope list.</summary>
    public static readonly TextKey Grants = new("team.roleEditor.grants", "Grants: {0}");

    public static readonly TextKey GrantsNothing = new("team.roleEditor.grantsNothing", "Grants no scopes");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Placeholder, Grants, GrantsNothing];
}

/// <summary>Localizable strings rendered by <c>ScopeOverrideEditor</c> — the per-principal scope picker.</summary>
public static class ScopeOverrideEditorText
{
    public static readonly TextKey Placeholder = new("team.scopeOverrideEditor.placeholder", "Add scopes...");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Placeholder];
}
