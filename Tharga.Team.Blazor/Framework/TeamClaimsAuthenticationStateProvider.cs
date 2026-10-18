using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

internal class TeamClaimsAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly AuthenticationStateProvider _inner;
    private readonly ITeamService _teamService;
    private readonly IUserService _userService;
    private readonly IScopeRegistry _scopeRegistry;
    private readonly ILocalStorageService _localStorage;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantRoleService _tenantRoleService;

    public TeamClaimsAuthenticationStateProvider(
        [FromKeyedServices("inner-auth-state")] AuthenticationStateProvider inner,
        ITeamService teamService,
        IUserService userService,
        ILocalStorageService localStorage,
        IHttpContextAccessor httpContextAccessor,
        IScopeRegistry scopeRegistry = null,
        ITenantRoleService tenantRoleService = null)
    {
        _inner = inner;
        _teamService = teamService;
        _userService = userService;
        _localStorage = localStorage;
        _httpContextAccessor = httpContextAccessor;
        _scopeRegistry = scopeRegistry;
        _tenantRoleService = tenantRoleService;

        _inner.AuthenticationStateChanged += task => NotifyAuthenticationStateChanged(task);
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var authState = await _inner.GetAuthenticationStateAsync();
        var principal = authState.User;
        var identity = principal.Identity as ClaimsIdentity;

        if (identity == null || !identity.IsAuthenticated)
            return authState;

        // Already enriched (by server-side TeamServerClaimsTransformation or a previous call)
        if (identity.HasClaim(c => c.Type == Constants.TeamKeyCookie))
            return authState;

        // If HttpContext is available, we're on the server — TeamServerClaimsTransformation
        // already ran in the HTTP pipeline. No need for JS interop.
        if (_httpContextAccessor.HttpContext != null)
            return authState;

        // No HttpContext = WASM client — use LocalStorage via JS interop
        string teamKey = null;
        try
        {
            teamKey = await _localStorage.GetItemAsStringAsync(Constants.SelectedTeamLocalStorageKey);
        }
        catch
        {
            return authState;
        }

        if (string.IsNullOrEmpty(teamKey))
            return authState;

        identity.AddClaim(new Claim(Constants.TeamKeyCookie, teamKey));

        var user = await _userService.GetCurrentUserAsync(principal);
        var member = await _teamService.GetTeamMemberAsync(teamKey, user?.Key);
        // A member with no access level gets no claims at all, as TeamGrantResolver answers for them: emitting
        // the rest would issue a "Team" role and an empty AccessLevel claim for a record nobody finished.
        if (member is { AccessLevel: { } level })
        {
            identity.AddClaim(new Claim(TeamClaimTypes.TeamKey, teamKey));
            identity.AddClaim(new Claim(ClaimTypes.Role, Roles.TeamMember));
            identity.AddClaim(new Claim(ClaimTypes.Role, $"Team{level}"));
            identity.AddClaim(new Claim(TeamClaimTypes.AccessLevel, level.ToString()));

            if (_tenantRoleService != null)
            {
                foreach (var scope in await _tenantRoleService.GetEffectiveScopesAsync(teamKey, level, member.TenantRoles, member.ScopeOverrides))
                {
                    identity.AddClaim(new Claim(TeamClaimTypes.Scope, scope));
                }
            }
            else if (_scopeRegistry != null)
            {
                foreach (var scope in _scopeRegistry.GetEffectiveScopes(level, member.TenantRoles, member.ScopeOverrides))
                {
                    identity.AddClaim(new Claim(TeamClaimTypes.Scope, scope));
                }
            }
        }

        return new AuthenticationState(principal);
    }
}
