namespace Tharga.Team;

/// <summary>
/// Defines a scope with its default minimum access level and an optional human-readable description
/// (shown as a tooltip in the scope picker).
/// </summary>
/// <param name="Name">The scope name, as checked by <see cref="RequireScopeAttribute"/>.</param>
/// <param name="DefaultMinimumLevel">
/// The least-privileged access level that is granted this scope automatically. Meaningless when
/// <paramref name="GrantOnly"/> is true, since no access level grants such a scope.
/// </param>
/// <param name="Description">Human-readable description, shown in the scope catalogue and pickers.</param>
/// <param name="GrantOnly">
/// When true the scope is registered for documentation and validation only: no access level grants it,
/// a tenant-defined custom role may not reference it, and the scope-override pickers do not offer it.
/// It is held solely through a code-registered tenant role or an explicit scope override, so holding it
/// is a recorded decision rather than a consequence of being a team Owner or Administrator.
/// Register one with <see cref="ScopeRegistry.RegisterGrantOnly"/>.
/// </param>
public record ScopeDefinition(string Name, AccessLevel DefaultMinimumLevel, string Description = null, bool GrantOnly = false);
