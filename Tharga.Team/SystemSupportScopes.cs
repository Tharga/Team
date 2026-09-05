namespace Tharga.Team;

/// <summary>
/// System-level scope constants for support cases that belong to no team.
/// </summary>
/// <remarks>
/// <b>These exist because <c>support:read</c> cannot govern an unassigned case.</b> That is a team scope, and
/// a case with no team has nothing to hold it against — so a case nobody has assigned would otherwise be
/// readable by nobody, which is the same as losing it.
/// <para>
/// <b>Deliberately separate from the team scopes rather than a widening of them.</b> Holding
/// <c>support:read</c> on one team must never confer sight of unassigned cases, which may concern any tenant
/// or none: that would let a member of the smallest team read everything that arrived by mail. Granting
/// these is a decision about the whole product, which is what a system scope is for.
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
}
