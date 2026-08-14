using Microsoft.AspNetCore.Http;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Records that an entry was produced while its author was simulating reduced access.
/// </summary>
/// <remarks>
/// <b>The actor is untouched.</b> Simulation removes scopes and roles and never the identity claims, so
/// an entry already names the real person — this only adds the context that makes an otherwise puzzling
/// record legible: why an administrator's action was refused, or why it was performed at a level below
/// the one they hold.
/// <para>
/// Registered as a singleton, like every <see cref="IAuditEnricher"/>, so the simulation is read through
/// <see cref="IHttpContextAccessor"/> rather than from a scoped dependency.
/// </para>
/// <para>
/// <b>An interactive component has no <c>HttpContext</c></b>, and reading that alone silently dropped the
/// metadata from every entry a Blazor Server host wrote — including all of the ones made during a demo,
/// which is when it is wanted (Tharga/Team#220). Inside a circuit the principal comes from
/// <see cref="AccessSimulationPrincipalAccessor"/> instead. Both paths then read the same claim, so there
/// is one place that decides what a simulation is rather than two that can disagree.
/// </para>
/// </remarks>
internal sealed class AccessSimulationAuditEnricher(
    IHttpContextAccessor httpContextAccessor,
    AccessSimulationPrincipalAccessor principalAccessor = null) : IAuditEnricher
{
    public void Enrich(AuditEntry entry, IDictionary<string, string> metadata)
    {
        var principal = httpContextAccessor?.HttpContext?.User ?? principalAccessor?.Current;
        if (principal == null) return;

        var simulation = AccessSimulationCookie.Read(principal.FindFirst(AccessSimulationCookie.ClaimType)?.Value);
        if (simulation == null) return;

        metadata[AccessSimulationMetadataKeys.Active] = "true";
        metadata[AccessSimulationMetadataKeys.Kind] = simulation.Kind.ToString();
        metadata[AccessSimulationMetadataKeys.Target] = simulation.Label ?? string.Empty;
    }
}

/// <summary>
/// Metadata keys written when an entry is produced under access simulation.
/// </summary>
/// <remarks>
/// Named rather than inlined, and dotted and lower-case like the rest of the audit vocabulary. A change
/// here changes the shape of the audit record, so these are part of the public contract.
/// </remarks>
public static class AccessSimulationMetadataKeys
{
    /// <summary>Present and <c>"true"</c> when the caller was simulating reduced access.</summary>
    public const string Active = "simulation.active";

    /// <summary>How the simulated access was named — a user, a role, scopes, or an access level.</summary>
    public const string Kind = "simulation.kind";

    /// <summary>What was being simulated: a member's name, a role name, or a level.</summary>
    public const string Target = "simulation.target";
}
