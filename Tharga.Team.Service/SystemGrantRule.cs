using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// The rule behind the grant ceiling on system API keys (Tharga/Team#522), as a question a view can ask: which of these scopes does the caller lack?
/// </summary>
/// <remarks>
/// <b>One rule, asked in two places.</b> The service enforces it and refuses; a view asks it so as not to offer what would be refused. The view's answer is a
/// convenience and the service's is the guard, so they must not drift: both read it here.
/// </remarks>
public static class SystemGrantRule
{
    /// <summary>The scopes among <paramref name="scopes"/> that <paramref name="caller"/> does not hold as system scopes; none for a holder of <see cref="SystemApiKeyScopes.Oversee"/>.</summary>
    public static string[] Missing(ClaimsPrincipal caller, IEnumerable<string> scopes)
    {
        if (caller?.HasClaim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee) == true) return [];

        var held = (caller?.FindAll(TeamClaimTypes.SystemScope).Select(c => c.Value) ?? []).ToHashSet(StringComparer.Ordinal);

        return (scopes ?? []).Where(s => !held.Contains(s)).Distinct().Order().ToArray();
    }

    /// <summary>Whether the caller holds everything in <paramref name="scopes"/>, so may grant them or act on a key that carries them.</summary>
    public static bool Holds(ClaimsPrincipal caller, IEnumerable<string> scopes) => Missing(caller, scopes).Length == 0;
}
