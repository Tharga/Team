using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// Reads the team header and, when a system caller names a team it may act on, adds the claims for it.
/// </summary>
/// <remarks>
/// <b>Claims rather than an ambient value, so nothing downstream has to know this exists.</b> Every
/// authorization path in the toolkit — <c>ScopeProxy</c>, <c>[RequireScope]</c>, <c>TeamScopePolicy</c> —
/// reads the principal. Adding <c>TeamKey</c> and <c>Scope</c> claims for the named team means a host's
/// own controllers are covered without a line of per-endpoint work, and REST and MCP agree by
/// construction rather than by being kept in step.
/// <para>
/// A contradiction — a team-bound credential naming a different team — is refused with <c>403</c> here
/// rather than left to a later check. The request has already said two incompatible things, and the
/// earliest honest answer is the best one.
/// </para>
/// </remarks>
public sealed class TeamContextMiddleware(RequestDelegate next, IOptions<TeamContextOptions> options)
{
    public async Task InvokeAsync(HttpContext context, TeamContextResolver resolver)
    {
        var headerName = options.Value.TeamKeyHeader;

        if (context.User?.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(headerName))
        {
            var headerTeamKey = context.Request.Headers.TryGetValue(headerName, out var values)
                ? values.ToString()?.Trim()
                : null;

            var resolved = await resolver.ResolveAsync(context.User, string.IsNullOrWhiteSpace(headerTeamKey) ? null : headerTeamKey);

            if (resolved.IsRefused)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync(resolved.Refusal == TeamContextRefusal.Contradiction
                    ? $"This credential is bound to one team; '{headerName}' named a different one."
                    : $"Team '{headerTeamKey}' has not consented to being acted on behalf of.");
                return;
            }

            // Only when the resolver granted scopes. A team-bound caller already carries its own, and
            // re-adding them would be a second, quieter place that decides what a team key may do.
            if (resolved.Scopes != null && !string.IsNullOrEmpty(resolved.TeamKey))
            {
                var claims = new List<Claim> { new(TeamClaimTypes.TeamKey, resolved.TeamKey) };
                claims.AddRange(resolved.Scopes.Select(s => new Claim(TeamClaimTypes.Scope, s)));
                if (!string.IsNullOrEmpty(resolved.MemberKey)) claims.Add(new Claim(TeamClaimTypes.MemberKey, resolved.MemberKey));

                context.User.AddIdentity(new ClaimsIdentity(claims, "TeamContext"));
            }
        }

        await next(context);
    }
}

/// <summary>Configuration for how a request names the team it acts on.</summary>
public sealed class TeamContextOptions
{
    /// <summary>
    /// Header a system API key names the target team in. Default <c>X-Team-Key</c>.
    /// </summary>
    /// <remarks>
    /// <b>The one place this name is configured.</b> MCP reads it from here too — it briefly had its own
    /// copy, which a host could have set on one surface and not the other, leaving the same call named
    /// differently depending on the door. That is the shape <c>ConsentOptions</c> had to be rescued from,
    /// and it is not worth repeating for a string.
    /// <para>
    /// A team API key needs no header at all: its team cannot be anything other than its own, and naming
    /// a different one is refused rather than ignored.
    /// </para>
    /// </remarks>
    public string TeamKeyHeader { get; set; } = "X-Team-Key";
}
