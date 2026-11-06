using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// What a caller may hand out: nothing beyond what they hold themselves (Tharga/Team#409).
/// </summary>
/// <remarks>
/// <b>One rule, one place, for both surfaces that grant</b> — API keys (<see cref="ApiKeyManagementService"/>) and
/// members (<see cref="AuthorizationTeamServiceDecorator"/>). Holding <c>apikey:manage</c> or <c>member:manage</c>
/// says a caller may <i>administer</i>; it did not say how much they may give. Latent while only Administrators
/// held those scopes, an escalation as soon as a host handed either to a lower level, role or override: mint an
/// Administrator key, or raise your own row.
/// <para>
/// What a grant would carry is composed by <see cref="TeamGrantResolver"/>, the one copy of that rule, so the
/// ceiling cannot disagree with the claims it protects.
/// </para>
/// <para>
/// <b>Who is exempt:</b> an Owner or Administrator of the team already holds every registered scope, so the
/// subset test would only ever refuse them the grant-only scopes — which exist to be handed out. For keys,
/// <see cref="SystemApiKeyScopes.Oversee"/> is the audit escape and exempts too.
/// </para>
/// </remarks>
internal sealed class GrantCeiling
{
    private readonly IScopeRegistry _scopeRegistry;
    private readonly TeamGrantResolver _resolver;

    public GrantCeiling(IScopeRegistry scopeRegistry, ITenantRoleService tenantRoleService)
    {
        _scopeRegistry = scopeRegistry;
        _resolver = new TeamGrantResolver(null, scopeRegistry, tenantRoleService);
    }

    /// <summary>
    /// Whether the caller may give anything: an Owner or Administrator of <paramref name="teamKey"/>, or, for keys,
    /// holder of the oversight scope.
    /// </summary>
    public static bool IsExempt(ClaimsPrincipal caller, string teamKey, bool oversightExempts)
    {
        var level = TeamScopePolicy.AccessLevelFor(caller, teamKey);
        if (level is AccessLevel.Owner or AccessLevel.Administrator) return true;

        return oversightExempts && TeamScopePolicy.HasSystemScope(caller, SystemApiKeyScopes.Oversee);
    }

    /// <summary>
    /// Refuses names that are not registered scopes, for every caller. An unregistered override is a grant of
    /// nothing today and of something the day a scope by that name is registered.
    /// </summary>
    public void EnsureOverridesRegistered(IEnumerable<string> overrides)
    {
        if (_scopeRegistry == null || overrides == null) return;

        var known = _scopeRegistry.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = overrides.Where(o => !known.Contains(o)).Distinct().Order().ToArray();
        if (unknown.Length == 0) return;

        throw new InvalidOperationException($"These are not registered scopes and cannot be granted: {string.Join(", ", unknown)}.");
    }

    /// <summary>
    /// Refuses a grant of <paramref name="level"/>, <paramref name="roles"/> and <paramref name="overrides"/> that
    /// holds anything the caller does not.
    /// </summary>
    public async Task EnsureCanGrantAsync(ClaimsPrincipal caller, string teamKey, AccessLevel? level, IEnumerable<string> roles, IEnumerable<string> overrides, bool oversightExempts)
    {
        EnsureOverridesRegistered(overrides);
        if (IsExempt(caller, teamKey, oversightExempts)) return;

        // Owner and Administrator carry more than their scopes (what ownership and administration mean), so a
        // caller below them never hands either out, whatever the registry says the level holds.
        if (level is AccessLevel.Owner or AccessLevel.Administrator)
            throw new UnauthorizedAccessException($"Only an Owner or Administrator of the team can grant the {level} level.");

        var missing = await MissingAsync(caller, teamKey, level, roles, overrides);
        if (missing.Length == 0) return;

        throw new UnauthorizedAccessException(
            $"This would grant scopes you do not hold yourself: {string.Join(", ", missing)}. You can only grant what you hold.");
    }

    /// <summary>
    /// Refuses touching something that holds more than the caller does: a key or member above them is not theirs
    /// to change, lock, delete or reveal.
    /// </summary>
    public async Task EnsureCanChangeAsync(ClaimsPrincipal caller, string teamKey, AccessLevel? targetLevel, IEnumerable<string> targetRoles, IEnumerable<string> targetOverrides, bool oversightExempts)
    {
        if (IsExempt(caller, teamKey, oversightExempts)) return;

        if (targetLevel is AccessLevel.Owner or AccessLevel.Administrator)
            throw new UnauthorizedAccessException($"This holds the {targetLevel} level, which only an Owner or Administrator of the team can change.");

        var missing = await MissingAsync(caller, teamKey, targetLevel, targetRoles, targetOverrides);
        if (missing.Length == 0) return;

        throw new UnauthorizedAccessException(
            $"This holds scopes you do not hold yourself ({string.Join(", ", missing)}), so it is not yours to change.");
    }

    /// <summary>Whether <paramref name="caller"/> holds everything the target does, for a UI deciding what to offer.</summary>
    public async Task<bool> CanChangeAsync(ClaimsPrincipal caller, string teamKey, AccessLevel? targetLevel, IEnumerable<string> targetRoles, IEnumerable<string> targetOverrides, bool oversightExempts)
    {
        try
        {
            await EnsureCanChangeAsync(caller, teamKey, targetLevel, targetRoles, targetOverrides, oversightExempts);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<string[]> MissingAsync(ClaimsPrincipal caller, string teamKey, AccessLevel? level, IEnumerable<string> roles, IEnumerable<string> overrides)
    {
        var wanted = await _resolver.ResolveScopesAsync(teamKey, level, roles ?? [], overrides ?? []);
        var held = TeamScopePolicy.TeamScopesFor(caller, teamKey);

        return wanted.Where(s => !held.Contains(s)).Distinct().Order().ToArray();
    }
}
