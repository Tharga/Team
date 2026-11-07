namespace Tharga.Team;

/// <summary>
/// Registry of all known scopes. Used at runtime to resolve effective scopes for an access level.
/// </summary>
public interface IScopeRegistry
{
    IReadOnlyList<ScopeDefinition> All { get; }

    /// <summary>The groups scopes are shown under, in the order the host defined them. Empty when the host defined none.</summary>
    IReadOnlyList<ScopeGroup> Groups => Array.Empty<ScopeGroup>();

    /// <summary>The scopes <paramref name="accessLevel"/> grants on its own. Empty for null: an unset level grants nothing.</summary>
    IReadOnlyList<string> GetScopesForAccessLevel(AccessLevel? accessLevel);

    /// <summary>
    /// The scopes a member holds from their level, roles and overrides. <b>Empty for a null level</b>, roles and
    /// overrides included: an unset level is a record nobody finished, and its extras must not make it worth something.
    /// </summary>
    IReadOnlyList<string> GetEffectiveScopes(AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides = null);

    /// <summary>
    /// <see cref="GetScopesForAccessLevel(AccessLevel?)"/> for a given kind of holder: for an API key, scopes registered
    /// for <see cref="ScopePrincipals.Members"/> only are left out (Tharga/Team#412).
    /// </summary>
    /// <remarks>
    /// <b>Every place that composes a key's scopes must call this, not the overload without a kind.</b> The default
    /// here ignores the kind so a host's own registry keeps compiling, which means such a registry does not filter
    /// anything; <see cref="ScopeRegistry"/> does.
    /// </remarks>
    IReadOnlyList<string> GetScopesForAccessLevel(AccessLevel? accessLevel, PrincipalKind kind) => GetScopesForAccessLevel(accessLevel);

    /// <summary>
    /// <see cref="GetEffectiveScopes(AccessLevel?, IEnumerable{string}, IEnumerable{string})"/> for a given kind of
    /// holder. Only the scopes the level supplies are filtered; roles and overrides are added as named.
    /// </summary>
    IReadOnlyList<string> GetEffectiveScopes(AccessLevel? accessLevel, IEnumerable<string> roleNames, IEnumerable<string> scopeOverrides, PrincipalKind kind)
        => GetEffectiveScopes(accessLevel, roleNames, scopeOverrides);
}
