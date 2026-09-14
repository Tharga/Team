using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>Why a request could not act on the team it named.</summary>
public enum TeamContextRefusal
{
    /// <summary>Resolved; no refusal.</summary>
    None = 0,

    /// <summary>A team-bound credential named a different team.</summary>
    Contradiction,

    /// <summary>The team does not exist, or has granted no consent.</summary>
    NotConsented,
}

/// <summary>
/// Which team a request is about, and what the caller may do there.
/// </summary>
/// <param name="TeamKey">The resolved team, or null when the request is not about one.</param>
/// <param name="Scopes">Scopes to grant for it, or null when the caller's own claims already carry them.</param>
/// <param name="Refusal">Why it could not be resolved. <see cref="TeamContextRefusal.None"/> on success.</param>
public sealed record TeamContext(string TeamKey, IReadOnlyList<string> Scopes, TeamContextRefusal Refusal)
{
    public bool IsRefused => Refusal != TeamContextRefusal.None;

    internal static TeamContext None { get; } = new(null, null, TeamContextRefusal.None);
    internal static TeamContext Refused(TeamContextRefusal reason) => new(null, null, reason);
}

/// <summary>
/// Resolves the team a request acts on, from the credential and an optional header. <b>The single place
/// that answers it</b> — REST and MCP both call this rather than each working it out.
/// </summary>
/// <remarks>
/// <b>An external caller never names a team in a parameter.</b> A team API key is bound to one team and
/// can be nothing else, so a parameter beside it would be a second source of truth for one question —
/// they can disagree, and an API shaped to allow that is wrong even though the disagreement is refused.
/// <para>
/// A system key is bound to none, so it says which team it acts on behalf of in a header, and the team
/// must have consented. What it may do there is the <i>consented level</i>, exactly as for a person
/// reaching a team they do not belong to.
/// </para>
/// <para>
/// <b>Consent for a key is the team's level, not a role match.</b> A key holds no roles, so the question
/// asked is whether the team consented at all. Worth stating plainly: a team that enables consent for its
/// support staff thereby admits system keys too, at that same level.
/// </para>
/// </remarks>
public sealed class TeamContextResolver
{
    private readonly ITeamService _teamService;
    private readonly IUserService _userService;
    private readonly IScopeRegistry _scopeRegistry;
    private readonly ITenantRoleService _tenantRoleService;
    private readonly AccessLevel _defaultConsentLevel;

    public TeamContextResolver(
        ITeamService teamService,
        IScopeRegistry scopeRegistry = null,
        ITenantRoleService tenantRoleService = null,
        IUserService userService = null,
        AccessLevel defaultConsentLevel = AccessLevel.Viewer)
    {
        _teamService = teamService;
        _scopeRegistry = scopeRegistry;
        _tenantRoleService = tenantRoleService;
        _userService = userService;
        _defaultConsentLevel = defaultConsentLevel;
    }

    /// <summary>
    /// Resolves the team for <paramref name="principal"/>, given the team named in the header (or null).
    /// </summary>
    /// <remarks>
    /// The four cases, in the order they are decided:
    /// <list type="number">
    /// <item>A team-bound caller with a header naming a different team — <b>refused</b>. It is a
    /// contradiction, not a preference; ignoring it would leave the caller believing they asked for
    /// something they did not get.</item>
    /// <item>A team-bound caller — its own team, with the scopes already on its claims.</item>
    /// <item>No header — no team context. A system caller operates system-wide, which is what its system
    /// grants authorize.</item>
    /// <item>A header — the named team, at its consented level, or refused if it has not consented.</item>
    /// </list>
    /// </remarks>
    public async Task<TeamContext> ResolveAsync(ClaimsPrincipal principal, string headerTeamKey)
    {
        // Bound means "issued for one team and can be nothing else" -- a team API key. A *user* also
        // carries a TeamKey claim for the team they selected, but that is a choice, not a binding:
        // naming another team they belong to is re-selection, not a contradiction. Treating the two
        // alike would refuse something MCP has always allowed.
        var isApiKey = principal?.FindFirst(TeamClaimTypes.ApiKeyId) != null
                       || principal?.HasClaim(TeamClaimTypes.IsSystemKey, "true") == true;

        var boundTeamKey = isApiKey ? principal?.FindFirst(TeamClaimTypes.TeamKey)?.Value : null;

        if (!string.IsNullOrEmpty(boundTeamKey))
        {
            if (!string.IsNullOrEmpty(headerTeamKey) &&
                !string.Equals(boundTeamKey, headerTeamKey, StringComparison.OrdinalIgnoreCase))
            {
                return TeamContext.Refused(TeamContextRefusal.Contradiction);
            }

            // Its own team. The scopes are already on the claims, so nothing is granted here.
            return new TeamContext(boundTeamKey, null, TeamContextRefusal.None);
        }

        if (string.IsNullOrEmpty(headerTeamKey)) return TeamContext.None;

        // A person naming a team is answered by membership first, then by the consent their roles carry
        // -- TeamGrantResolver, the copy of that rule the claims builder already uses. Only a key, which
        // has neither membership nor roles, falls through to the team-level consent below.
        if (!isApiKey)
        {
            var user = _userService == null ? null : await _userService.GetCurrentUserAsync(principal);
            var grant = await new TeamGrantResolver(_teamService, _scopeRegistry, _tenantRoleService)
                .ResolveAsync(principal, user?.Key, headerTeamKey, _defaultConsentLevel);

            return grant == null
                ? TeamContext.Refused(TeamContextRefusal.NotConsented)
                : new TeamContext(headerTeamKey, grant.Scopes, TeamContextRefusal.None);
        }

        var team = await _teamService.GetTeamByKeyAsync(headerTeamKey);
        if (team == null) return TeamContext.Refused(TeamContextRefusal.NotConsented);

        // Consent is expressed by naming roles; a team that has named none has consented to nothing, and
        // the level alone does not amount to an invitation.
        var consent = TeamConsent.Resolve(team, DateTime.UtcNow);
        if (!consent.HasConsent) return TeamContext.Refused(TeamContextRefusal.NotConsented);

        var level = consent.AccessLevel ?? AccessLevel.Viewer;

        var scopes = _tenantRoleService != null
            ? await _tenantRoleService.GetEffectiveScopesAsync(headerTeamKey, level, [], [])
            : _scopeRegistry?.GetEffectiveScopes(level, [], []) ?? [];

        return new TeamContext(headerTeamKey, [.. scopes], TeamContextRefusal.None);
    }
}
