using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// What a caller may hand out as a system API key's scopes: registered ones, and nothing beyond what they hold themselves (Tharga/Team#522).
/// </summary>
/// <remarks>
/// <b>The system-key counterpart of <see cref="GrantCeiling"/></b>, for the same reason: holding <c>apikeys:system:manage</c> says a caller may
/// <i>administer</i> system keys, not how much they may give. Without it a manager holding few scopes could mint a key carrying any, which
/// is an escalation as soon as the scope is held by anybody who is not already allowed everything.
/// <para>
/// <b>Who is exempt:</b> a holder of <see cref="SystemApiKeyScopes.Oversee"/>, the same escape the team ceiling gives keys, may grant any
/// <i>registered</i> scope. Registration is checked for everybody, since a name nothing registered grants nothing today and something the day a
/// scope by that name is registered.
/// </para>
/// </remarks>
internal sealed class SystemGrantCeiling(ISystemScopeRegistry registry)
{
    /// <summary>Refuses names that are not registered system scopes, for every caller.</summary>
    public void EnsureRegistered(IEnumerable<string> scopes)
    {
        if (registry == null || scopes == null) return;

        var known = registry.All.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = scopes.Where(s => !known.Contains(s)).Distinct().Order().ToArray();
        if (unknown.Length == 0) return;

        throw new InvalidOperationException($"These are not registered scopes and cannot be granted: {string.Join(", ", unknown)}.");
    }

    /// <summary>Refuses a grant of <paramref name="scopes"/> that holds anything the caller does not.</summary>
    public void EnsureCanGrant(ClaimsPrincipal caller, IEnumerable<string> scopes)
    {
        EnsureRegistered(scopes);

        var missing = MissingFor(caller, scopes);
        if (missing.Length == 0) return;

        throw new UnauthorizedAccessException(
            $"This would grant scopes you do not hold yourself: {string.Join(", ", missing)}. You can only grant what you hold.");
    }

    /// <summary>Refuses touching a key that holds more than the caller does: it is not theirs to change.</summary>
    public void EnsureCanChange(ClaimsPrincipal caller, IEnumerable<string> targetScopes)
    {
        var missing = MissingFor(caller, targetScopes);
        if (missing.Length == 0) return;

        throw new UnauthorizedAccessException(
            $"This key holds scopes you do not hold yourself ({string.Join(", ", missing)}), so it is not yours to change.");
    }

    private static string[] MissingFor(ClaimsPrincipal caller, IEnumerable<string> scopes)
    {
        if (caller?.HasClaim(TeamClaimTypes.SystemScope, SystemApiKeyScopes.Oversee) == true) return [];

        var held = (caller?.FindAll(TeamClaimTypes.SystemScope).Select(c => c.Value) ?? []).ToHashSet(StringComparer.Ordinal);

        return (scopes ?? []).Where(s => !held.Contains(s)).Distinct().Order().ToArray();
    }
}
