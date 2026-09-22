namespace Tharga.Team;

/// <summary>
/// System-level scope constants for support cases: the unassigned queue, and reading or answering across
/// every team.
/// </summary>
/// <remarks>
/// <b>These exist because a team scope cannot govern either population.</b> <c>support:read</c> is held
/// against a team, so a case with no team has nothing to hold it against, and a caller who belongs to no
/// team holds it nowhere.
/// <para>
/// <b>Deliberately separate from the team scopes rather than a widening of them.</b> Holding
/// <c>support:read</c> on one team must never confer sight of unassigned cases, which may concern any tenant
/// or none: that would let a member of the smallest team read everything that arrived by mail. Granting
/// these is a decision about the whole product, which is what a system scope is for.
/// </para>
/// <para>
/// <b>And deliberately two pairs rather than one.</b> The unassigned queue and every team's cases are
/// different populations, so holding one must not confer the other — the same reasoning that keeps both off
/// the team scopes. <see cref="AllRead"/> and <see cref="AllManage"/> are the strongest grants in the
/// package, which is why they are nameable and grantable on their own.
/// </para>
/// </remarks>
public static class SystemSupportScopes
{
    /// <summary>
    /// Read support cases that belong to no team, and list them.
    /// </summary>
    /// <remarks>
    /// The same privilege boundary as <c>support:read</c> and for the same reason — a case holds whatever
    /// somebody typed into it — but across the cases no team owns.
    /// </remarks>
    public const string Read = "support:unassigned:read";

    /// <summary>
    /// Reply to, close, reopen and <b>assign</b> a case that belongs to no team.
    /// </summary>
    /// <remarks>
    /// Assignment is the operation that matters here: it decides which tenant a case and its whole
    /// transcript become part of, so it is granted with answering rather than with reading.
    /// </remarks>
    public const string Manage = "support:unassigned:manage";

    /// <summary>
    /// Read and list support cases in <b>any</b> team, without belonging to it.
    /// </summary>
    /// <remarks>
    /// For the people who run support: staff of the product, who belong to none of the customers' teams and
    /// should not be put in them. Membership does not scale past a handful of tenants and puts staff in
    /// tenant membership lists; access simulation is a deliberate reduction of access and would record a
    /// member as having acted when a member did not.
    /// <para>
    /// This reads every conversation every customer has had, so it belongs to a support function rather than
    /// to an access level.
    /// </para>
    /// </remarks>
    public const string AllRead = "support:all:read";

    /// <summary>
    /// Reply to, close, reopen and hand over a support case in <b>any</b> team, without belonging to it.
    /// </summary>
    /// <remarks>
    /// Satisfies a read as well, exactly as <c>support:manage</c> does on a team — whoever may answer a case
    /// may read it.
    /// <para>
    /// <b>It does not grant assignment.</b> Assigning decides which tenant an unassigned case and its whole
    /// transcript become part of, which is <see cref="Manage"/>'s operation and stays there.
    /// </para>
    /// </remarks>
    public const string AllManage = "support:all:manage";
}
