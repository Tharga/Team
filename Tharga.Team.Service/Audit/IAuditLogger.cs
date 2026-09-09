namespace Tharga.Team.Service.Audit;

/// <summary>
/// Interface for audit logging. Implementations handle storage (MongoDB, ILogger, etc.).
/// </summary>
public interface IAuditLogger
{
    void Log(AuditEntry entry);
    Task<AuditQueryResult> QueryAsync(AuditQuery query);
}

/// <summary>
/// Query parameters for retrieving audit entries.
/// Supports both single-value and multi-value filters.
/// </summary>
public record AuditQuery
{
    // Single-value filters (kept for backwards compat and simple queries)
    public string TeamKey { get; init; }
    public string CallerIdentity { get; init; }

    /// <summary>Filters entries by the API key Guid string that authenticated the caller (matches <see cref="AuditEntry.CallerKeyId"/>).</summary>
    public string CallerKeyId { get; init; }

    /// <summary>Filters entries by the acting user's authentication subject (matches <see cref="AuditEntry.CallerUserIdentity"/>). Exact match, unlike <see cref="CallerIdentity"/>.</summary>
    public string CallerUserIdentity { get; init; }

    public string MethodName { get; init; }
    public string Feature { get; init; }
    public string Action { get; init; }
    public AuditCallerSource? CallerSource { get; init; }
    public AuditCallerType? CallerType { get; init; }
    public AuditEventType? EventType { get; init; }
    public bool? Success { get; init; }

    // Multi-value filters (take precedence over single-value when set)
    public string[] TeamKeys { get; init; }

    /// <summary>Filters to entries whose <see cref="AuditEntry.Feature"/> is one of these. Takes precedence over <see cref="Feature"/>.</summary>
    public string[] Features { get; init; }

    /// <summary>Filters to entries whose <see cref="AuditEntry.Action"/> is one of these. Takes precedence over <see cref="Action"/>.</summary>
    public string[] Actions { get; init; }

    /// <summary>Filters to entries whose <see cref="AuditEntry.ScopeChecked"/> is one of these. Entries a consumer wrote check no scope, so naming any scope here excludes all of them.</summary>
    public string[] Scopes { get; init; }

    /// <summary>Filters to entries of these types. Takes precedence over <see cref="EventType"/>.</summary>
    public AuditEventType[] EventTypes { get; init; }

    /// <summary>
    /// Excludes entries whose <see cref="AuditEntry.ScopeChecked"/> is one of these — the access traces a
    /// reader does not want, named directly rather than by enumerating everything else.
    /// </summary>
    /// <remarks>
    /// <b>An entry that checked no scope is kept.</b> Exclusion is by value, and a consumer-written entry
    /// has no <c>ScopeChecked</c> at all, so excluding <c>audit:read</c> removes the log's readers from the
    /// view without touching the domain entries beside them — which is the whole point of naming the
    /// exclusion instead of building an include list.
    /// <para>
    /// Combines with <see cref="Scopes"/> rather than overriding it: both apply, so an include list can be
    /// narrowed further. Naming the same scope in both yields nothing, which is the honest answer.
    /// </para>
    /// </remarks>
    public string[] ExcludedScopes { get; init; }

    /// <summary>
    /// Excludes entries of these types. Combines with <see cref="EventTypes"/> and <see cref="EventType"/>
    /// rather than overriding them.
    /// </summary>
    public AuditEventType[] ExcludedEventTypes { get; init; }

    // Paging and sorting
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 100;
    public string SortField { get; init; }
    public bool SortDescending { get; init; } = true;
}

/// <summary>
/// Result of an audit query with items and total count for paging.
/// </summary>
public record AuditQueryResult
{
    public IReadOnlyList<AuditEntry> Items { get; init; } = Array.Empty<AuditEntry>();
    public int TotalCount { get; init; }
}
