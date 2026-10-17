namespace Tharga.Team.Sample.Framework;

/// <summary>
/// Demonstrates <see cref="ITeamScopeVisibilityProvider"/>: two per-team features whose scopes are hidden
/// for teams that do not have them. A feature is "switched on" for a team whose name contains the feature's
/// name, so renaming a team turns it on or off.
/// </summary>
/// <remarks>
/// With a feature off, its scopes are not offered by the member, API-key and custom-role pickers, and the
/// access page (/scopes) lists no rows for them; the CaseOfficer role, which grants only <c>case:read</c>,
/// drops off its role bar. A real application decides from its own per-team settings, and also refuses the
/// scopes at runtime while the feature is off.
/// </remarks>
public sealed class SampleFeatureScopeVisibility(ITeamService teamService) : ITeamScopeVisibilityProvider
{
    private static readonly (string Feature, string Scope)[] FeatureScopes =
    [
        ("Billing", "billing:manage"),
        ("Case", "case:read"),
    ];

    public async Task<bool> IsScopeVisibleAsync(string teamKey, string scope, CancellationToken cancellationToken = default)
    {
        var feature = FeatureScopes.FirstOrDefault(x => x.Scope == scope).Feature;
        if (feature == null) return true;

        var team = await teamService.GetTeamByKeyAsync(teamKey);
        return team?.Name?.Contains(feature, StringComparison.OrdinalIgnoreCase) == true;
    }
}
