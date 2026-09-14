using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Tharga.Team.MongoDB;

/// <summary>
/// A <see cref="TeamAccessRequest"/> as stored inside its team document.
/// </summary>
/// <remarks>
/// A separate type so the enums carry <c>[BsonRepresentation(BsonType.String)]</c>; the contract in
/// <c>Tharga.Team</c> has no storage dependency to put the attribute on. Stored on the team so approving a request
/// and setting the team's consent are one document update.
/// </remarks>
public sealed record TeamAccessRequestEntity
{
    public required string Id { get; init; }
    public required string RequesterKey { get; init; }

    [BsonIgnoreIfNull]
    public string RequesterName { get; init; }

    [BsonRepresentation(BsonType.String)]
    public required AccessLevel AccessLevel { get; init; }

    [BsonIgnoreIfNull]
    public TimeSpan? Duration { get; init; }

    [BsonIgnoreIfNull]
    public string Message { get; init; }

    public required DateTime RequestedAt { get; init; }

    [BsonRepresentation(BsonType.String)]
    public TeamAccessRequestStatus Status { get; init; }

    [BsonIgnoreIfNull]
    public string DecidedBy { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? DecidedAt { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? GrantedUntil { get; init; }

    public TeamAccessRequest ToContract() => new()
    {
        Id = Id,
        RequesterKey = RequesterKey,
        RequesterName = RequesterName,
        AccessLevel = AccessLevel,
        Duration = Duration,
        Message = Message,
        RequestedAt = RequestedAt,
        Status = Status,
        DecidedBy = DecidedBy,
        DecidedAt = DecidedAt,
        GrantedUntil = GrantedUntil
    };

    public static TeamAccessRequestEntity FromContract(TeamAccessRequest request) => new()
    {
        Id = request.Id,
        RequesterKey = request.RequesterKey,
        RequesterName = request.RequesterName,
        AccessLevel = request.AccessLevel,
        Duration = request.Duration,
        Message = request.Message,
        RequestedAt = request.RequestedAt,
        Status = request.Status,
        DecidedBy = request.DecidedBy,
        DecidedAt = request.DecidedAt,
        GrantedUntil = request.GrantedUntil
    };
}

/// <summary>A <see cref="Team.TemporaryConsent"/> as stored inside its team document.</summary>
public sealed record TemporaryConsentEntity
{
    public required DateTime ExpiresAt { get; init; }

    [BsonIgnoreIfNull]
    public string[] PreviousConsentedRoles { get; init; }

    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public AccessLevel? PreviousConsentAccessLevel { get; init; }

    public TemporaryConsent ToContract() => new()
    {
        ExpiresAt = ExpiresAt,
        PreviousConsentedRoles = PreviousConsentedRoles,
        PreviousConsentAccessLevel = PreviousConsentAccessLevel
    };

    public static TemporaryConsentEntity FromContract(TemporaryConsent consent) => consent == null ? null : new()
    {
        ExpiresAt = consent.ExpiresAt,
        PreviousConsentedRoles = consent.PreviousConsentedRoles,
        PreviousConsentAccessLevel = consent.PreviousConsentAccessLevel
    };
}
