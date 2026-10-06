using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Mcp;
using Tharga.Team;
using Tharga.Team.Service;

namespace Tharga.Team.Mcp;

/// <summary>
/// <see cref="IMcpContextAccessor"/> implementation that builds an <see cref="IMcpContext"/> from the current
/// <see cref="HttpContext"/> on demand. Replaces the default AsyncLocal-backed accessor when <c>AddTeam</c>
/// is registered.
/// </summary>
/// <remarks>
/// This is also where a team named on the call is resolved — the one place the MCP pieces already agree
/// on, since <c>TeamMcpContext</c>, <c>McpScopeChecker</c> and the scope derivation below all read the
/// same team key. Resolving it anywhere else would mean resolving it three times.
/// <para>
/// The setter is a no-op: the context is derived from the HTTP request, not assigned.
/// </para>
/// </remarks>
public sealed class HttpContextMcpContextAccessor : IMcpContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly McpTeamOptions _options;
    private readonly ConsentOptions _consent;
    private readonly TeamContextOptions _teamContext;

    /// <remarks>
    /// <b>Only singletons may be injected here.</b> This type is registered as a singleton, so taking
    /// <c>IUserService</c> or <c>ITeamService</c> in the constructor captures a scoped service inside a
    /// singleton — which <c>ValidateOnBuild</c> refuses, stopping the application from starting at all.
    /// The team services are resolved per call from <c>HttpContext.RequestServices</c> instead: that is
    /// the request's own scope, and the only correct source for them here.
    /// <para>
    /// <paramref name="consent"/> is resolved rather than duplicated — it is the same instance the Blazor
    /// claims builder reads, so a caller reaches a team at the same level over MCP as through the UI. It
    /// is optional only so this package works without the Blazor registration, and it is safe to hold in
    /// a field because options are singletons.
    /// </para>
    /// </remarks>
    public HttpContextMcpContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        IOptions<McpTeamOptions> options,
        IOptions<ConsentOptions> consent = null,
        IOptions<TeamContextOptions> teamContext = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _consent = consent?.Value ?? new ConsentOptions();
        _teamContext = teamContext?.Value ?? new TeamContextOptions();
    }

    public IMcpContext Current
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx == null) return null;

            var user = ctx.User;
            var selectedTeamKey = ReadSelectedTeamKey(ctx);

            // Derive scope from claims so the Tharga.Mcp dispatcher's hierarchy filter
            // (p.Scope <= current.Scope, since Tharga.Mcp 0.1.2) can see providers at the
            // caller's level and below.
            //
            // - Developer role (or system API key) → System scope sees everything
            // - TeamKey claim, or a team named on this call → Team scope sees Team + User providers
            // - Otherwise → User scope
            var scope =
                (user?.IsInRole(_options.DeveloperRole) ?? false)
                    || (user?.HasClaim(TeamClaimTypes.IsSystemKey, "true") ?? false)
                    ? McpScope.System
                : !string.IsNullOrEmpty(user?.FindFirst(TeamClaimTypes.TeamKey)?.Value)
                    || !string.IsNullOrEmpty(selectedTeamKey)
                    ? McpScope.Team
                : McpScope.User;

            var identityResolver = ctx.RequestServices?.GetService<UserIdentityResolver>();

            if (string.IsNullOrEmpty(selectedTeamKey))
                return new TeamMcpContext(user, scope, _options.DeveloperRole, identityResolver);

            // Resolved synchronously because IMcpContextAccessor.Current is a property. The alternative
            // is an async seam through every provider signature, which is the same cost the header was
            // chosen to avoid; the common path reads the member cache, and a selecting call has already
            // paid for an HTTP round trip.
            var resolved = ResolveContextAsync(ctx, user, selectedTeamKey).GetAwaiter().GetResult();

            // Refused, not silently empty. The caller named a specific team, and an empty answer would
            // read as "that team has nothing in it" rather than "you cannot see it". Everywhere else in
            // this surface seeing nothing is the correct answer; here it would be a misleading one.
            if (resolved == null || resolved.IsRefused)
            {
                throw new UnauthorizedAccessException(resolved?.Refusal == TeamContextRefusal.Contradiction
                    ? $"This credential is bound to one team; '{_teamContext.TeamKeyHeader}' named a different one."
                    : $"Team '{selectedTeamKey}' is not available to this caller. Either it does not exist, " +
                      "or the caller has neither membership nor consent for it.");
            }

            return new TeamMcpContext(user, scope, _options.DeveloperRole, identityResolver, selectedTeamKey, resolved.Scopes);
        }
        set
        {
            // No-op: context is derived from HttpContext, not assigned.
        }
    }

    private string ReadSelectedTeamKey(HttpContext ctx)
    {
        if (string.IsNullOrEmpty(_teamContext.TeamKeyHeader)) return null;
        if (!ctx.Request.Headers.TryGetValue(_teamContext.TeamKeyHeader, out var values)) return null;

        var value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <remarks>
    /// Goes through <see cref="TeamContextResolver"/> — the one place that answers which team a request
    /// acts on — so REST and MCP cannot drift. It resolves a person by membership then consent, and a key
    /// by the team's consented level; this class no longer knows the difference.
    /// <para>
    /// Resolved from the request's own scope, never a captured field: those services are scoped and this
    /// class is a singleton.
    /// </para>
    /// </remarks>
    private async Task<TeamContext> ResolveContextAsync(HttpContext ctx, System.Security.Claims.ClaimsPrincipal principal, string teamKey)
    {
        var services = ctx.RequestServices;
        var teamService = services?.GetService<ITeamService>();
        if (teamService == null) return null;

        var resolver = new TeamContextResolver(
            teamService,
            services.GetService<IScopeRegistry>(),
            services.GetService<ITenantRoleService>(),
            services.GetService<IUserService>(),
            _consent.AccessLevel);

        return await resolver.ResolveAsync(principal, teamKey);
    }
}
