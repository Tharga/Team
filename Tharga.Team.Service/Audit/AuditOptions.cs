namespace Tharga.Team.Service.Audit;

/// <summary>
/// Configuration for audit logging.
/// </summary>
public class AuditOptions
{
    /// <summary>Where to store audit entries. Default: Logger only.</summary>
    public AuditStorageMode StorageMode { get; set; } = AuditStorageMode.Logger;

    /// <summary>Which caller sources to log. Default: Api and Web.</summary>
    public AuditCallerFilter CallerFilter { get; set; } = AuditCallerFilter.Api | AuditCallerFilter.Web;

    /// <summary>Which event types to log. Default: All.</summary>
    public AuditEventFilter EventFilter { get; set; } = AuditEventFilter.All;

    /// <summary>Actions to exclude from logging (e.g. "read", "list", "get"). Default: empty.</summary>
    public string[] ExcludedActions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// What a scope- or access-level-checked call records when its attribute says nothing.
    /// Default: <see cref="AuditMode.Access"/> — every such call is traced, as it always has been.
    /// </summary>
    /// <remarks>
    /// <b>Set this to <see cref="AuditMode.None"/> to make silence the default and record by exception.</b>
    /// A host under privacy rules that treat broad read logging as a hazard wants exactly that: nothing is
    /// recorded unless a method asks for it with <c>[RequireScope(..., Audit = AuditMode.Access)]</c>, so
    /// adding a method cannot silently start recording who read what.
    /// <para>
    /// <b>Why this is not the shipped default.</b> Flipping it centrally would end the access trace of every
    /// existing host with no compile error and nothing in a diff to notice — an audit trail that quietly
    /// stopped several releases ago. The choice belongs to the host, taken once and visibly.
    /// </para>
    /// <para>
    /// This governs entries the enforcement proxies write. It does not touch
    /// <see cref="ExcludedActions"/>, <see cref="EventFilter"/> or <see cref="CallerFilter"/>, which still
    /// filter whatever is produced — and it never suppresses a denial.
    /// </para>
    /// </remarks>
    public AuditMode DefaultAuditMode { get; set; } = AuditMode.Access;

    /// <summary>Endpoints to exclude from logging (e.g. "/health"). Default: empty.</summary>
    public string[] ExcludedEndpoints { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Days to retain audit entries in MongoDB, applied as a TTL index (<c>Timestamp_TTL</c>). Default: 90.
    /// <c>null</c> (or any value &lt;= 0) means **keep forever** — no TTL index is created. Note: removing
    /// or changing the TTL on an existing collection may require dropping the old <c>Timestamp_TTL</c>
    /// index manually, as MongoDB does not drop it automatically.
    /// </summary>
    public int? RetentionDays { get; set; } = 90;

    /// <summary>Batch size for background MongoDB writer. Default: 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Flush interval for background MongoDB writer in seconds. Default: 5.</summary>
    public int FlushIntervalSeconds { get; set; } = 5;
}
