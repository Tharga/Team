using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Tharga.MongoDB;

namespace Tharga.Team.Service;

/// <summary>
/// Default MongoDB entity for API keys.
/// </summary>
public record ApiKeyEntity : EntityBase, IApiKey
{
    /// <inheritdoc />
    public required string Key { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    [BsonIgnoreIfDefault]
    public string ApiKey { get; init; }

    /// <inheritdoc />
    [BsonIgnoreIfDefault]
    public string TeamKey { get; init; }

    /// <inheritdoc />
    [BsonIgnoreIfNull]
    public string[] SystemScopes { get; init; }

    /// <inheritdoc />
    [BsonIgnoreIfNull]
    public string CreatedBy { get; init; }

    /// <inheritdoc />
    [BsonIgnoreIfNull]
    public string OwnerMemberKey { get; init; }

    /// <inheritdoc />
    [BsonIgnoreIfNull]
    [BsonSerializer(typeof(TagListBsonSerializer))]
    public IReadOnlyList<Tag> Tags { get; init; }

    /// <summary>Hashed value of the API key used for verification.</summary>
    public required string ApiKeyHash { get; init; }

    /// <summary>
    /// Indexed lookup value, so authenticating a key verifies one candidate. The first 16 bytes of the key's
    /// SHA-256 as hex. An older key holds the first 8 characters of the raw key, a prefix every key of its team
    /// shares, until its first use replaces them.
    /// </summary>
    [BsonIgnoreIfNull]
    public string ApiKeyPrefix { get; init; }

    /// <summary>Access level for this API key. Null defaults to Administrator.</summary>
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.String)]
    public AccessLevel? AccessLevel { get; init; }

    /// <summary>Tenant roles assigned to this API key.</summary>
    [BsonIgnoreIfNull]
    public string[] Roles { get; init; }

    /// <summary>Individual scope overrides (additional scopes beyond AccessLevel and roles).</summary>
    [BsonIgnoreIfNull]
    public string[] ScopeOverrides { get; init; }

    /// <summary>Expiry date. Null means no expiry.</summary>
    [BsonIgnoreIfNull]
    public DateTime? ExpiryDate { get; init; }

    /// <summary>When this key was created.</summary>
    [BsonIgnoreIfNull]
    public DateTime? CreatedAt { get; init; }

    /// <summary>When this key was last used to authenticate. Null if never used.</summary>
    [BsonIgnoreIfNull]
    public DateTime? LastUsedAt { get; init; }

    /// <summary>When this key was disabled. Null means enabled.</summary>
    [BsonIgnoreIfNull]
    public DateTime? DisabledAt { get; init; }

    /// <summary>Who disabled this key. Null means enabled.</summary>
    [BsonIgnoreIfNull]
    public string DisabledBy { get; init; }
}
