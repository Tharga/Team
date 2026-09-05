using System.Security.Claims;

namespace Tharga.Team.Service;

/// <summary>
/// Resolves the current caller's <see cref="ClaimsPrincipal"/> for scope / access-level enforcement.
/// Abstracting this lets enforcement work outside an HTTP request — e.g. in an interactive Blazor Server
/// circuit where there is no <c>HttpContext</c> but the principal is available via
/// <c>AuthenticationStateProvider</c>. The default implementation reads <c>IHttpContextAccessor</c>.
/// </summary>
public interface ITeamPrincipalAccessor
{
    /// <summary>The current principal, or null when no caller can be resolved.</summary>
    ValueTask<ClaimsPrincipal> GetCurrentAsync();
}
