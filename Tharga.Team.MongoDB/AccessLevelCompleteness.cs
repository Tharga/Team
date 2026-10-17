using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Finds stored team members that carry no access level. Since 4.0 they read as null and are granted nothing
/// until someone sets a level; before 4.0 they were treated as <see cref="AccessLevel.Owner"/>.
/// </summary>
/// <remarks>
/// <b>Why this is a store-level query and not a check over <see cref="ITeamMember"/>.</b> Before 4.0 a
/// document with no such field deserialized to <c>Owner</c>, the enum's zero value, exactly like a member
/// deliberately stored as one, so only the stored document could tell them apart. Since 4.0 the level is
/// nullable and they deserialize apart, but the store query stays: one count finds them without loading every
/// team.
/// <para>
/// <b>This reports; it does not repair.</b> Choosing a level for an ambiguous member is a decision about
/// authorization: the cautious-looking guess (<see cref="AccessLevel.Viewer"/>) silently demotes anyone who
/// really was an owner, and the faithful-looking guess (<c>Owner</c>) preserves the very grant being
/// investigated. Only the host knows which, so the toolkit names the teams and leaves the write alone.
/// </para>
/// <para>
/// <b>It changes no behaviour.</b> Since 4.0 an affected member is already refused, by the nullable level
/// itself. This exists so a host notices who lost access on upgrade — typically a team's original owner,
/// stored before the field existed — and restores them deliberately.
/// </para>
/// </remarks>
internal static class AccessLevelCompleteness
{
    /// <summary>
    /// Teams containing at least one member document with no access-level field.
    /// </summary>
    /// <remarks>
    /// A <see cref="FilterDefinition{TDocument}"/> rather than an <c>Expression</c> predicate, and it has to
    /// be: <c>$exists</c> has no LINQ equivalent, so the typed query overloads cannot express field absence
    /// however well their signatures otherwise fit. That is the whole reason this filter is built by hand.
    /// </remarks>
    public static FilterDefinition<TTeamEntity> MembersWithNoAccessLevel<TTeamEntity, TMember>()
        where TTeamEntity : TeamEntityBase<TMember>
        where TMember : TeamMemberBase
    {
        return Builders<TTeamEntity>.Filter.ElemMatch(
            x => x.Members,
            Builders<TMember>.Filter.Exists(m => m.AccessLevel, false));
    }
}
