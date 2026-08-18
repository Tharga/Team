using Tharga.Team;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// Builds an <see cref="AccessSimulation"/> from each of the ways a target can be named.
/// </summary>
/// <remarks>
/// The four kinds are not four features. Each names a <b>target scope set</b>, and the simulation is
/// always the same operation afterwards: keep what the target has and the caller also has, remove the
/// rest. Keeping the construction in one place is what makes that true rather than aspirational.
/// <para>
/// <b>Every kind drops system scopes and application roles.</b> They were originally dropped only when
/// simulating a user — the reasoning being that another person's system access cannot be computed — but
/// that left the other three kinds keeping the caller's own system-wide grants, so simulating "Viewer"
/// still showed every team and the cross-team audit log. A simulation is for seeing <i>less</i>; a
/// system-wide grant surviving one defeats it whatever the target was named. Reported from the sample,
/// 2026-08-03.
/// </para>
/// <para>
/// The consequence is worth stating: a simulation shows access <b>within the selected team</b>, never a
/// faithful reproduction of someone's system-wide reach. <c>AccessSimulationDifference</c> says so for a
/// user target, where it is unknowable rather than merely dropped.
/// </para>
/// </remarks>
internal static class AccessSimulationTargets
{
    /// <summary>
    /// Another member of the selected team, from the grant they actually hold there.
    /// </summary>
    public static AccessSimulation FromUser(string label, AccessLevel accessLevel, IEnumerable<string> scopes)
        => new()
        {
            Kind = AccessSimulationKind.User,
            Label = label,
            Scopes = [.. scopes ?? []],
            AccessLevel = accessLevel,
            DropSystemScopes = true,
            DropAppRoles = true
        };

    /// <summary>
    /// A tenant role. Its scopes become the whole effective set — applying a role <b>replaces</b> rather
    /// than adds, which is what makes it a simulation rather than a grant.
    /// </summary>
    /// <remarks>
    /// No access level is set. A role says nothing about a level, and inventing one would be a second
    /// assumption on top of the one the caller made.
    /// </remarks>
    public static AccessSimulation FromRole(string roleName, IEnumerable<string> scopes)
        => new()
        {
            Kind = AccessSimulationKind.Role,
            Label = roleName,
            Scopes = [.. scopes ?? []],
            DropSystemScopes = true,
            DropAppRoles = true
        };

    /// <summary>An explicit set of scopes, exactly as given.</summary>
    public static AccessSimulation FromScopes(IEnumerable<string> scopes)
    {
        var list = (scopes ?? []).ToArray();

        return new AccessSimulation
        {
            Kind = AccessSimulationKind.Scopes,
            Label = list.Length == 0 ? "no scopes" : string.Join(", ", list),
            Scopes = list,
            DropSystemScopes = true,
            DropAppRoles = true
        };
    }

    /// <summary>
    /// The label recorded for a demo-mode simulation, in the banner and in the audit metadata.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not localized.</b> It is written to <c>simulation.target</c> on every audit entry
    /// produced during a demo, and metadata whose value depends on the operator's language cannot be
    /// searched or compared across a deployment. The card's visible wording is a separate, localizable
    /// string.
    /// </remarks>
    public const string DemoLabel = "Demo mode";

    /// <summary>
    /// Demo mode: the caller's own team access, with their system-wide access dropped.
    /// </summary>
    /// <remarks>
    /// The one target that is not a *replacement*. The other four name someone else's access; this one
    /// names the caller's own, so the intersection removes nothing team-side and the only thing that
    /// changes is the system half — which is what <see cref="AccessSimulation.DropSystemScopes"/> and
    /// <see cref="AccessSimulation.DropAppRoles"/> already do for every kind.
    /// <para>
    /// <b>No access level is set</b>, which is what keeps the team access identical. Passing one would run
    /// it through the clamp and could lower the level the caller holds on this team — the opposite of
    /// "show me exactly what an ordinary member of this team sees, minus my system privileges".
    /// </para>
    /// <para>
    /// Recorded as <see cref="AccessSimulationKind.Scopes"/> rather than a kind of its own, so this adds no
    /// public API. The consequence is that <c>simulation.kind</c> reads <c>Scopes</c> in the audit log;
    /// <c>simulation.target</c> carries <see cref="DemoLabel"/>, which is what distinguishes a demo from a
    /// hand-picked scope simulation.
    /// </para>
    /// </remarks>
    /// <param name="ownTeamScopes">
    /// The caller's own scopes on the selected team — <c>AccessSimulationState.GetOwnScopesAsync</c>.
    /// Anything narrower would silently reduce their team access as well as their system access.
    /// </param>
    public static AccessSimulation FromDemo(IEnumerable<string> ownTeamScopes)
        => new()
        {
            Kind = AccessSimulationKind.Demo,
            Label = DemoLabel,
            Scopes = [.. ownTeamScopes ?? []],
            DropSystemScopes = true,
            DropAppRoles = true
        };

    /// <summary>An access level, with the scopes that level grants.</summary>
    public static AccessSimulation FromAccessLevel(AccessLevel accessLevel, IEnumerable<string> scopes)
        => new()
        {
            Kind = AccessSimulationKind.AccessLevel,
            Label = accessLevel.ToString(),
            Scopes = [.. scopes ?? []],
            AccessLevel = accessLevel,
            DropSystemScopes = true,
            DropAppRoles = true
        };
}
