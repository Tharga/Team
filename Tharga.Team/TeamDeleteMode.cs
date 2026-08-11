namespace Tharga.Team;

/// <summary>
/// What <c>DeleteTeamAsync</c> does to a team.
/// </summary>
/// <remarks>
/// <b>Stored nowhere</b> — this is a host's configuration, not a property of a team, so it can be changed
/// without a migration. A team that was soft-deleted while the mode was <see cref="Soft"/> stays
/// soft-deleted if the host later switches to <see cref="Hard"/>; the switch governs new deletions only.
/// </remarks>
public enum TeamDeleteMode
{
    /// <summary>
    /// Mark the team deleted and hide it from every read. The default.
    /// </summary>
    /// <remarks>
    /// Recoverable, and — the reason it is the default — it needs no elevated database privilege. Dropping
    /// a team's storage is confined to <c>PurgeTeamAsync</c>, so a deployment whose database user cannot
    /// drop databases can still delete teams (Tharga/Team#224).
    /// </remarks>
    Soft,

    /// <summary>
    /// Remove the team record and drop its storage immediately, as versions before 3.13.1 did.
    /// </summary>
    /// <remarks>
    /// Irreversible, and it requires whatever privilege the storage adapter needs to drop a team's data —
    /// for the MongoDB adapter in a per-team-database deployment, that is <c>dropDatabase</c>. Choose it
    /// only where a deletion genuinely must leave nothing behind.
    /// </remarks>
    Hard
}
