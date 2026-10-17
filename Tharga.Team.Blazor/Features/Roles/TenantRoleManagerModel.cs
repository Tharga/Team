using Tharga.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Roles;

/// <summary>
/// Mutable working copy of a custom role while it is being edited in <c>TenantRoleManager</c>.
/// </summary>
public sealed class EditableTenantRole
{
    private readonly IReadOnlyList<string> _heldScopes = [];
    private readonly IReadOnlySet<string> _hiddenScopes;

    public string Name { get; set; } = "";
    public string Description { get; set; }

    /// <summary>The scopes the editor shows and changes: the role's scopes, minus any hidden for the team.</summary>
    public List<string> Scopes { get; set; } = [];

    public EditableTenantRole() { }

    public EditableTenantRole(TenantRoleDefinition role) : this(role, null) { }

    /// <param name="role">The stored role.</param>
    /// <param name="hiddenScopes">
    /// The scopes the team's <see cref="ITeamScopeVisibilityProvider"/> hides right now. They are left out of
    /// <see cref="Scopes"/>, so the picker never shows them, and the ones the role holds are kept on save.
    /// </param>
    public EditableTenantRole(TenantRoleDefinition role, IReadOnlySet<string> hiddenScopes)
    {
        Name = role.Name;
        Description = role.Description;
        _heldScopes = role.Scopes?.ToList() ?? [];
        _hiddenScopes = hiddenScopes;
        Scopes = _heldScopes.Where(x => hiddenScopes == null || !hiddenScopes.Contains(x)).ToList();
    }

    /// <remarks>
    /// <b>A hidden scope the role held is written back.</b> The editor could neither see nor remove it, so
    /// switching a feature off for a team must not strip that feature's scopes from the team's roles; switching
    /// it on again finds them still there.
    /// </remarks>
    public TenantRoleDefinition ToDefinition() => new(
        Name?.Trim() ?? "",
        ScopeVisibility.Persist(Scopes, inherited: [], _heldScopes, _hiddenScopes),
        string.IsNullOrWhiteSpace(Description) ? null : Description.Trim());
}

/// <summary>
/// The editing working-set behind <c>TenantRoleManager</c>: load current custom roles, add/remove them,
/// validate client-side (mirroring the server's privilege-escalation/collision guard for immediate
/// feedback), and project back to <see cref="TenantRoleDefinition"/> for persistence.
/// </summary>
public sealed class TenantRoleManagerModel
{
    public List<EditableTenantRole> Roles { get; private set; } = [];

    public void Load(IEnumerable<TenantRoleDefinition> roles)
        => Load(roles, null);

    /// <summary>Loads the roles for editing, leaving <paramref name="hiddenScopes"/> out of the editor and keeping them on save.</summary>
    public void Load(IEnumerable<TenantRoleDefinition> roles, IReadOnlySet<string> hiddenScopes)
        => Roles = (roles ?? []).Select(r => new EditableTenantRole(r, hiddenScopes)).ToList();

    public EditableTenantRole AddNew()
    {
        var role = new EditableTenantRole();
        Roles.Add(role);
        return role;
    }

    public void Remove(EditableTenantRole role) => Roles.Remove(role);

    public IReadOnlyList<TenantRoleDefinition> ToDefinitions()
        => Roles.Select(r => r.ToDefinition()).ToList();

    /// <summary>
    /// Client-side validation for immediate feedback: names must be non-empty, unique, and must not collide
    /// with a code-registered role name. Scope selection is constrained to registered scopes by the UI, so
    /// the server remains the authority on the privilege-escalation guard.
    /// </summary>
    public IReadOnlyList<string> Validate(IEnumerable<string> codeRoleNames)
    {
        var errors = new List<string>();
        var codeNames = new HashSet<string>(codeRoleNames ?? [], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in Roles)
        {
            var name = role.Name?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add("A role name must not be empty.");
                continue;
            }
            if (!seen.Add(name))
                errors.Add($"Duplicate role name '{name}'.");
            if (codeNames.Contains(name))
                errors.Add($"Role '{name}' collides with a built-in role of the same name.");
        }

        return errors;
    }
}
