namespace Tharga.Team;

/// <summary>
/// Decides whether a given scope should be offered for a given team. Consulted by the scope pickers
/// (team members' scope overrides, team API keys, custom roles) before listing scopes, and by the access
/// page (<c>ScopeView</c>) before listing its rows, letting a consumer hide the scopes of a feature that is
/// switched off for that team. The scope equivalent of <see cref="ITenantRoleVisibilityProvider"/>.
/// </summary>
/// <remarks>
/// Visibility is a UI concern only. Hiding a scope does not prune existing grants and does not affect scope
/// resolution — a member, key or custom role that already holds a hidden scope keeps it, and a picker that
/// saves while the scope is hidden writes it back unchanged, so switching the feature on again restores it.
/// Refusing the scope at runtime while its feature is off is the consumer's decision, made where the feature
/// is enforced. Make it, though: the access page leaves a hidden scope out as not applying to the team, so a
/// scope that is hidden but still honoured makes that page understate access. The default implementation
/// shows every scope.
/// </remarks>
public interface ITeamScopeVisibilityProvider
{
    /// <summary>
    /// Returns whether <paramref name="scope"/> should be offered and shown for the team identified by
    /// <paramref name="teamKey"/>.
    /// </summary>
    Task<bool> IsScopeVisibleAsync(string teamKey, string scope, CancellationToken cancellationToken = default);
}
