namespace Tharga.Team;

/// <summary>
/// Data-access consent options — controls cross-team access granted by a team to global roles.
/// </summary>
/// <remarks>
/// <b>In the core package, not the Blazor one, because consent is authorization rather than
/// presentation.</b> It decides what a caller may do in a team they do not belong to, and every surface
/// that answers that question has to agree — the Blazor circuit, and an MCP call naming a team. It lived
/// under <c>Tharga.Team.Blazor.Framework</c> until the MCP surface needed it too and could not reach it,
/// which briefly left the same policy configured in two places.
/// <para>
/// Resolve it as <c>IOptions&lt;ConsentOptions&gt;</c>. A host that never configures it gets these
/// defaults, so a package used without the Blazor registration still behaves predictably.
/// </para>
/// </remarks>
public class ConsentOptions
{
    /// <summary>
    /// Global roles that can be granted access to a team via consent. The consent toggle in TeamComponent
    /// offers these roles. Default ["Developer"].
    /// </summary>
    public string[] Roles { get; set; } = ["Developer"];

    /// <summary>Show the consent toggle in TeamComponent for team administrators. Default false.</summary>
    public bool ShowToggle { get; set; } = false;

    /// <summary>
    /// Also grant the roles in <see cref="Roles"/> the <see cref="SystemTeamScopes.Read"/> system scope,
    /// so they can enumerate every team rather than only the ones they belong to. Default false.
    /// </summary>
    /// <remarks>
    /// Off by default on purpose: <see cref="Roles"/> means "roles a team may grant access to", which is
    /// a per-team inbound opt-in. Deriving a global enumeration privilege from it automatically would
    /// silently widen access for existing hosts on upgrade. Opt in explicitly, or map the scope yourself
    /// via <c>ConfigureSystemRoles</c>. Grants discovery only — access inside a team remains governed by
    /// that team's consent.
    /// <para>
    /// Turning this on grants <see cref="SystemTeamScopes.Read"/>, so its prerequisite applies: the team service
    /// must be able to list every team. See that scope's remarks.
    /// </para>
    /// </remarks>
    public bool GrantTeamsRead { get; set; } = false;

    /// <summary>Whether new teams start with consent enabled. Default true.</summary>
    public bool Default { get; set; } = true;

    /// <summary>
    /// Default access level granted via team consent, used when the consent itself doesn't carry a level.
    /// Default Viewer.
    /// </summary>
    public AccessLevel AccessLevel { get; set; } = AccessLevel.Viewer;
}
