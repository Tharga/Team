namespace Tharga.Team;

/// <summary>
/// A store holding data that belongs to a member of a team, to be cleaned up when they stop being one.
/// </summary>
/// <remarks>
/// Runs after a member is removed, after they leave, and for every team a deleted user was in. The sibling of
/// <see cref="ITeamPurgeParticipant"/>, for the same reason: the removal sites live in a base class a host
/// derives from, so each store registers itself rather than the team service being taught about each.
/// <para>
/// <b>Runs after the removal, and its failure does not undo it.</b> The member is already gone, which is the
/// change that matters; a participant that fails is logged and the removal stands. So nothing a participant
/// does may be what keeps the departed member out — that has to be enforced where access is decided. For
/// private API keys it is: authentication refuses a key whose owner is no longer a member, on every request.
/// </para>
/// </remarks>
public interface ITeamMemberRemovalParticipant
{
    /// <summary>What this participant cleans up, for the log.</summary>
    string Name { get; }

    /// <summary>
    /// Cleans up what this store holds for <paramref name="userKey"/> in <paramref name="teamKey"/>. Returns how
    /// many records were affected. Must be safe to run again.
    /// </summary>
    Task<int> MemberRemovedAsync(string teamKey, string userKey, CancellationToken cancellationToken = default);
}
