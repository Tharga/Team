using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// The stored form of <see cref="InboundMailItem"/>. A mail with no <see cref="TeamKey"/> is held by the postmaster,
/// and archived there when its <see cref="Outcome"/> is <see cref="PostmasterMailOutcome.Archived"/>.
/// </summary>
public record InboundMailEntity : EntityBase
{
    /// <summary>Derived from the delivery itself, and unique, so the same delivery read twice is kept once.</summary>
    public required string MailId { get; init; }

    [BsonIgnoreIfNull]
    public string TeamKey { get; init; }

    [BsonIgnoreIfNull]
    public string ItemKey { get; init; }

    public required MailEnvelopeEntity Envelope { get; init; }

    [BsonRepresentation(BsonType.String)]
    public required MailRouteReason Reason { get; init; }

    public required DateTime ReceivedAt { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? KeyRetiresAt { get; init; }

    [BsonIgnoreIfNull]
    public string OwnerType { get; init; }

    [BsonIgnoreIfNull]
    public string OwnerId { get; init; }

    [BsonIgnoreIfNull]
    public string Outcome { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? HandledAt { get; init; }

    [BsonIgnoreIfNull]
    public string HandledBy { get; init; }
}
