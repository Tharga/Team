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
}
