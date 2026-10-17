using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

public abstract record TeamMemberBase : ITeamMember
{
    public string Key { get; set; }
    public string Name { get; init; }
    public Invitation Invitation { get; init; }
    public DateTime? LastSeen { get; init; }

    [BsonRepresentation(BsonType.String)]
    public MembershipState? State { get; init; }

    /// <summary>
    /// Null when no level was ever stored, and then grants nothing. Ignored when null, like <see cref="State"/>, so a
    /// missing field round-trips as missing rather than being written back as a value nobody chose.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    [BsonIgnoreIfNull]
    public AccessLevel? AccessLevel { get; init; }

    [BsonIgnoreIfNull]
    public string[] TenantRoles { get; init; }

    [BsonIgnoreIfNull]
    public string[] ScopeOverrides { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? SuspendedAt { get; init; }

    [BsonIgnoreIfNull]
    public string SuspendedBy { get; init; }
}