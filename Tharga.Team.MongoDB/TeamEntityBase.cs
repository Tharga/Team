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
}