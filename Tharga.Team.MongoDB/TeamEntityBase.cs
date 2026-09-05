using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

public abstract record TeamEntityBase<TTeamMemberModel> : EntityBase, ITeam<TTeamMemberModel>
    where TTeamMemberModel : TeamMemberBase
{
    public required string Key { get; init; }

    [BsonIgnoreIfNull]
    public string Icon { get; init; }

    public required string Name { get; init; }
    public TTeamMemberModel[] Members { get; init; }

    [BsonIgnoreIfNull]
    public string[] ConsentedRoles { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public AccessLevel? ConsentAccessLevel { get; init; }

    [BsonIgnoreIfNull]
    public IReadOnlyList<TenantRoleDefinition> CustomRoles { get; init; }

    /// <summary>
    /// When the team was soft-deleted, or <c>null</c> while it is live. See <see cref="ITeam.DeletedAt"/>.
    /// </summary>
    /// <remarks>
    /// <c>BsonIgnoreIfNull</c>, so a live team's document is byte-identical to one written before soft
    /// delete existed — nothing to migrate, and an existing document reads back as live because the field
    /// is absent.
    /// </remarks>
    [BsonIgnoreIfNull]
    public DateTime? DeletedAt { get; init; }

    /// <summary>Identity of whoever soft-deleted the team. See <see cref="ITeam.DeletedBy"/>.</summary>
    [BsonIgnoreIfNull]
    public string DeletedBy { get; init; }

    /// <summary>
    /// Whether the team is soft-deleted, derived from <see cref="DeletedAt"/> and never stored.
    /// </summary>
    /// <remarks>
    /// <c>BsonIgnore</c> is load-bearing: persisting it would create a second copy of the same fact, free
    /// to drift from the timestamp it is supposed to summarise. Queries filter on <see cref="DeletedAt"/>
    /// for the same reason — it is the stored truth.
    /// </remarks>
    [BsonIgnore]
    public bool IsDeleted => DeletedAt != null;

    /// <summary>The team's <see cref="Name"/>, so a team rendered as text names itself.</summary>
    /// <remarks>
    /// Replaces the record dump the compiler would otherwise synthesize. A UI control bound to a team
    /// falls back to <c>ToString()</c> for the text it cannot get from a display property — Radzen's
    /// dropdown does exactly this for its hidden accessible input — which put the entity id, the consent
    /// access level and every property a host had added onto the page (Tharga/Team#254).
    /// <para>
    /// <b><c>sealed</c> is load-bearing.</b> A record synthesizes its own <c>ToString()</c> in every
    /// declaration unless a base declares a sealed override, so without it each host's
    /// <c>record TeamEntity : TeamEntityBase&lt;TeamMember&gt;;</c> would silently regenerate the dump and
    /// the fix would reach nobody. The cost is deliberate and worth naming: a host can no longer give its
    /// team entity a <c>ToString()</c> of its own.
    /// </para>
    /// </remarks>
    public sealed override string ToString() => Name;
}