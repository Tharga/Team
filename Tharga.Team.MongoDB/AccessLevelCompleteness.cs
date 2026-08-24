using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>
/// Finds stored team members that carry no access level, and so are being treated as
/// <see cref="AccessLevel.Owner"/>.
/// </summary>
/// <remarks>
/// <b>Why this is a store-level query and not a check over <see cref="ITeamMember"/>.</b>
/// <see cref="AccessLevel"/> declares <see cref="AccessLevel.Owner"/> first, so it is the zero value — and a
/// document with no such field therefore deserializes to <c>Owner</c>, exactly like a member deliberately
/// stored as one. After deserialization the two are the same value and no amount of C# can separate them.
/// The distinction survives only in the stored document, as the presence or absence of a field, which is
/// where this looks.
/// <para>
/// <b>This reports; it does not repair.</b> Choosing a level for an ambiguous member is a decision about
/// authorization: the cautious-looking guess (<see cref="AccessLevel.Viewer"/>) silently demotes anyone who
/// really was an owner, and the faithful-looking guess (<c>Owner</c>) preserves the very grant being
/// investigated. Only the host knows which, so the toolkit names the teams and leaves the write alone.
/// </para>
/// <para>
/// <b>It changes no behaviour.</b> An affected member keeps being treated as <c>Owner</c> exactly as before.
/// The fix — an <c>AccessLevel?</c> whose absence grants nothing — is a breaking change queued for 4.0; this
/// exists so a host can find and correct its data before that release starts refusing those members.
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
