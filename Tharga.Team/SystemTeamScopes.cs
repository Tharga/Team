namespace Tharga.Team;

/// <summary>
/// System-level (cross-team) scope constants for team operations. Unlike the in-team <see cref="TeamScopes"/>
/// (which authorize only the caller's own team), these authorize across any team and are granted to system
/// API keys or privileged roles. The toolkit defines them; the consumer applies them as they see fit
/// (e.g. <c>o.ConfigureSystemRoles</c> mapping a role to the scope, or a system API key's scope list).
/// </summary>
public static class SystemTeamScopes
{
    /// <summary>
    /// Authorizes deleting <b>any</b> team, regardless of membership and regardless of the
    /// <c>AllowTeamCreation</c> self-service option. The unconditional, cross-team delete path.
    /// </summary>
    public const string Delete = "teams:delete";

    /// <summary>
    /// Authorizes permanently removing a soft-deleted team, including dropping its storage.
    /// </summary>
    /// <remarks>
    /// <b>Separate from <see cref="Delete"/> because it is the only irreversible one</b>, and because it is
    /// the only one needing whatever privilege the storage adapter requires to destroy a team's data — for
    /// the MongoDB adapter in a per-team-database deployment, <c>dropDatabase</c>. That privilege is one
    /// most managed deployments will not grant permanently: Atlas's <c>readWriteAnyDatabase</c> does not
    /// include it.
    /// <para>
    /// Splitting it means a deployment can withhold both this scope and the database grant and still delete
    /// teams normally, which is what Eplicta FortDocs asked for in Tharga/Team#224.
    /// </para>
    /// <para>
    /// <b>Restoring needs no scope of its own.</b> It is strictly less destructive than the delete it
    /// undoes, so <see cref="Delete"/> covers it — anyone trusted to remove a team is trusted to change
    /// their mind. A third scope would be a grant nobody has asked for and one more thing to map.
    /// </para>
    /// </remarks>
    public const string Purge = "teams:purge";

    /// <summary>
    /// Authorizes enumerating <b>any</b> team via <c>ITeamService.GetAllTeamsAsync</c>, regardless of
    /// membership — the discovery path for oversight roles (support, administration).
    /// </summary>
    /// <remarks>
    /// Discovery only. Holding this grants no access <i>inside</i> a team: selecting a team the caller
    /// is not a member of still yields only the scopes that team has consented to, and none if it has
    /// consented to nothing. Contrast with the in-team <see cref="TeamScopes.Read"/>, which authorizes
    /// reading the caller's own team.
    /// </remarks>
    public const string Read = "teams:read";

    /// <summary>
    /// Authorizes giving an <b>ownerless</b> team an owner, chosen from that team's existing members.
    /// The repair path for a team whose owner was deleted.
    /// </summary>
    /// <remarks>
    /// <b>Why this is its own scope.</b> The rule this codebase already applies is that a new scope is
    /// warranted when an operation is irreversible or crosses a tenant boundary. This does the second:
    /// the caller is by definition not a member of the team being repaired, since a member could not have
    /// reached this state. It is also privilege escalation by construction — it hands someone `Owner` —
    /// so it earns its own grant and its own audit entry rather than riding on a scope granted for
    /// something else.
    /// <para>
    /// Deliberately <b>not</b> <see cref="Delete"/>: authorizing repair with the right to destroy would
    /// mean the only way to fix a team is to hold the right to delete it. Deliberately not the in-team
    /// <c>team:manage</c> either, which is `TeamKey`-bound and so cannot fit a non-member.
    /// </para>
    /// <para>
    /// <b>The operation refuses when the team already has an owner</b>, which is what keeps this a repair
    /// rather than a way to take over a healthy team. With no sitting owner there is nobody to escalate
    /// past, so the invariant <c>SetMemberRoleAsync</c> protects stays intact.
    /// </para>
    /// </remarks>
    public const string AssignOwner = "teams:assign-owner";

    /// <summary>
    /// Authorizes renaming <b>any</b> team and setting or clearing its icon, regardless of membership.
    /// The oversight equivalent of the in-team <c>team:manage</c>, for the two operations that are
    /// presentational.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately narrower than in-team <c>team:manage</c>, which also covers consent and custom
    /// roles.</b> Those are authorization: consent is a team's own statement about what it exposes
    /// inbound, and an operator overriding it is a far larger claim than fixing a typo in a name. Rename
    /// and icon change how a team looks; consent changes who can reach it.
    /// <para>
    /// Extending this scope to consent would need a deliberate decision and its own name. An
    /// architecture-level rule cannot express "these two members of that scope but not those two", so a
    /// test asserts the boundary instead.
    /// </para>
    /// </remarks>
    public const string Manage = "teams:manage";
}
