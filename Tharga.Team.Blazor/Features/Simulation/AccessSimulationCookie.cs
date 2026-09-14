using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Reads and writes the active simulation as a cookie value.
/// </summary>
/// <remarks>
/// <b>The value is untrusted and is not signed.</b> A caller can edit it freely, and that is acceptable
/// only because <see cref="AccessSimulationFilter"/> can do nothing but remove — naming a scope here that
/// the caller does not hold achieves nothing. If the filter ever gained the ability to add, this cookie
/// would become a privilege-escalation vector; the two facts are load-bearing together.
/// <para>
/// Parsing therefore never throws. A malformed, truncated or hand-edited value means "no simulation",
/// which returns the caller to their real access — the safe direction, and the same outcome as clearing
/// the cookie.
/// </para>
/// </remarks>
internal static class AccessSimulationCookie
{
    /// <summary>Named beside <c>selected_team_id</c>: the other session-scoped, client-visible cookie.</summary>
    public const string Name = "access_simulation";

    /// <summary>
    /// Claim type the cookie value is stamped onto, so the simulation travels on the principal.
    /// </summary>
    /// <remarks>
    /// The revalidator runs inside a SignalR circuit where there is no <c>HttpContext</c> and no cookie
    /// to read. The selected team already solves this the same way — read once at the HTTP boundary,
    /// carried on the principal thereafter. Holding the raw cookie value rather than a parsed shape keeps
    /// it obviously untrusted at every point it is used.
    /// </remarks>
    public const string ClaimType = "access_simulation";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Whether <paramref name="principal"/> is currently viewing under a simulation.
    /// </summary>
    /// <remarks>
    /// For components that read state the simulation cannot reach. Simulation filters <b>claims</b>; a
    /// component that queries the store directly sees the caller's real record however thoroughly their
    /// claims were narrowed. Those components have to ask, and this is the question.
    /// </remarks>
    public static bool IsActive(System.Security.Claims.ClaimsPrincipal principal)
        => Read(principal?.FindFirst(ClaimType)?.Value) != null;

    /// <summary>
    /// Parses a cookie value and returns the simulation only if it belongs to <paramref name="selectedTeamKey"/>.
    /// </summary>
    /// <remarks>
    /// <b>The one place the team binding is decided</b>, used by both claim-issuance paths so they cannot disagree
    /// about whether a simulation is in force (Tharga/Team#276). A simulation with no team key — written before
    /// simulations carried one — applies to no team, which leaves the caller with their real access.
    /// </remarks>
    public static AccessSimulation ReadForTeam(string value, string selectedTeamKey)
    {
        if (string.IsNullOrEmpty(selectedTeamKey)) return null;

        var simulation = Read(value);
        return simulation != null && string.Equals(simulation.TeamKey, selectedTeamKey, StringComparison.Ordinal)
            ? simulation
            : null;
    }

    /// <summary>Serializes a simulation for storage. Null clears it.</summary>
    public static string Write(AccessSimulation simulation)
    {
        if (simulation == null) return string.Empty;

        var json = JsonSerializer.Serialize(simulation, Options);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Parses a cookie value, or returns null for anything that is not a well-formed simulation.
    /// </summary>
    public static AccessSimulation Read(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            var simulation = JsonSerializer.Deserialize<AccessSimulation>(json, Options);

            // A simulation with no label is still applied -- the label is presentation only, and refusing
            // one would turn a cosmetic problem into a silent return to full access.
            return simulation == null ? null : simulation with { Scopes = simulation.Scopes ?? [] };
        }
        catch (Exception e) when (e is FormatException or JsonException or DecoderFallbackException)
        {
            return null;
        }
    }
}
