using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Tharga.MongoDB;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// MongoDB entity for audit log entries.
/// </summary>
public record AuditEntryEntity : EntityBase
{
    public required DateTime Timestamp { get; init; }
    public Guid CorrelationId { get; init; }

    [BsonRepresentation(BsonType.String)]
    public AuditEventType EventType { get; init; }

    public string Feature { get; init; }
    public string Action { get; init; }
    public string MethodName { get; init; }
    public long DurationMs { get; init; }
    public bool Success { get; init; }

    [BsonIgnoreIfNull]
    public string ErrorMessage { get; init; }

    [BsonRepresentation(BsonType.String)]
    public AuditCallerType CallerType { get; init; }

    public string CallerIdentity { get; init; }

    [BsonIgnoreIfNull]
    public string CallerKeyId { get; init; }

    public string CallerUserIdentity { get; init; }

    [BsonIgnoreIfNull]
    public string TargetKind { get; init; }

    [BsonIgnoreIfNull]
    public string TargetKey { get; init; }

    public string TeamKey { get; init; }
    public string AccessLevel { get; init; }

    [BsonRepresentation(BsonType.String)]
    public AuditCallerSource CallerSource { get; init; }

    [BsonIgnoreIfNull]
    public string ScopeChecked { get; init; }

    [BsonRepresentation(BsonType.String)]
    public AuditScopeResult ScopeResult { get; init; }

    [BsonIgnoreIfNull]
    public Dictionary<string, string> Metadata { get; init; }
}
