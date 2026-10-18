namespace Tharga.Team;

/// <summary>
/// Default <see cref="ITeamScopeVisibilityProvider"/> that makes every scope visible for every team.
/// Registered unless a consumer supplies its own provider, keeping the visibility hook non-breaking.
/// </summary>
public sealed class AllScopesVisibleTeamScopeVisibilityProvider : ITeamScopeVisibilityProvider
{
    public Task<bool> IsScopeVisibleAsync(string teamKey, string scope, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
