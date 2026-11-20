namespace Tharga.Team.Service.Audit;

/// <summary>
/// Represents a single audit log entry. Immutable record created by the audit infrastructure.
/// </summary>
public record AuditEntry
{
    // Core
    public required DateTime Timestamp { get; init; }
    public Guid CorrelationId { get; init; }
    public required AuditEventType EventType { get; init; }

    // Operation
    public string Feature { get; init; }
    public string Action { get; init; }
    public string MethodName { get; init; }
    public long DurationMs { get; init; }
    public bool Success { get; init; } = true;
    public string ErrorMessage { get; init; }

    // Identity
    public AuditCallerType CallerType { get; init; }
    public string CallerIdentity { get; init; }

    /// <summary>
    /// Stable identifier of the API key that authenticated this caller (the <c>IApiKey.Key</c> Guid string).
    /// Null when the caller did not authenticate via an API key.
    /// </summary>
    public string CallerKeyId { get; init; }

    /// <summary>
    /// The authentication subject of the user who acted (<see cref="IUser.Identity"/>), or null for an
    /// API key, a background actor, or an unknown caller.
    /// </summary>
    /// <remarks>
    /// <see cref="CallerIdentity"/> is a display string resolved through a fallback chain — name claim,
    /// then preferred_username, then subject — so its content depends on which claims the identity
    /// provider emits, and a filter matching it has to do so by substring. This is always the subject or
    /// nothing, which makes an exact match possible. Same reasoning as <see cref="CallerKeyId"/>, which
    /// exists because a key's display name was "human-friendly but not unique".
    /// </remarks>
    public string CallerUserIdentity { get; init; }

    /// <summary>
    /// What kind of thing the operation was done to — one of <see cref="AuditTargetKinds"/> — or null when the
    /// entry has no single target. Read together with <see cref="TargetKey"/>.
    /// </summary>
    /// <remarks>
    /// The <b>target</b> is who or what was acted on, as opposed to the caller, who acted. It is what makes
    /// "who deleted this user?" or "who changed this person's access?" a query instead of a scan of
    /// <see cref="Metadata"/>. Entries written before this existed, and entries of operations with no single
    /// target, have none, so a filter on a target never matches them.
    /// </remarks>
    public string TargetKind { get; init; }

    /// <summary>
    /// The key of the target: the user's key, the team's key or the API key's id, as named by <see cref="TargetKind"/>.
    /// Exact-matchable. Not an authentication subject — for a user it is <see cref="IUser.Key"/>, the key the application
    /// uses everywhere, which is not what <see cref="CallerUserIdentity"/> holds.
    /// </summary>
    public string TargetKey { get; init; }

    public string TeamKey { get; init; }
    public string AccessLevel { get; init; }
    public AuditCallerSource CallerSource { get; init; }

    // Authorization
    public string ScopeChecked { get; init; }
    public AuditScopeResult ScopeResult { get; init; }

    // Extensibility
    public Dictionary<string, string> Metadata { get; init; }

    /// <summary>
    /// Parses a scope string "feature:action" into Feature and Action components.
    /// </summary>
    public static (string Feature, string Action) ParseScope(string scope)
    {
        if (string.IsNullOrEmpty(scope)) return (null, null);
        var parts = scope.Split(':', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (scope, null);
    }
}
